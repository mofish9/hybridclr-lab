#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <vector>

#include "Baselib.h"
#include "C/Baselib_SystemSemaphore.h"
#include "C/Baselib_SystemFutex.h"
#include "C/Baselib_Thread.h"

#include "utils/Memory.h"
#include "vm/Assembly.h"
#include "vm/Class.h"
#include "vm/Exception.h"
#include "vm/Image.h"
#include "vm/MetadataCache.h"
#include "metadata/GenericMetadata.h"
#include "il2cpp-runtime-stats.h"
#include "hybridclr/interpreter/Interpreter.h"
#include "hybridclr/interpreter/InterpreterModule.h"
#include "hybridclr/metadata/AOTHomologousImage.h"
#include "hybridclr/metadata/MetadataUtil.h"
#include "native_test_hooks.h"

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#else
#include <cerrno>
#include <climits>
#include <linux/futex.h>
#include <semaphore.h>
#include <sys/syscall.h>
#include <time.h>
#include <unistd.h>
#endif

BASELIB_C_INTERFACE
{
#if !defined(_WIN32)
    void Baselib_SystemFutex_Wait(int32_t* address, int32_t expected, uint32_t timeoutInMilliseconds)
    {
        timespec timeout;
        timespec* timeoutPointer = nullptr;
        if (timeoutInMilliseconds != UINT32_MAX)
        {
            timeout.tv_sec = timeoutInMilliseconds / 1000;
            timeout.tv_nsec = (timeoutInMilliseconds % 1000) * 1000000L;
            timeoutPointer = &timeout;
        }
        ::syscall(__NR_futex, address, FUTEX_WAIT_PRIVATE, expected, timeoutPointer, nullptr, 0);
    }

    void Baselib_SystemFutex_Notify(int32_t* address, uint32_t count,
        Baselib_WakeupFallbackStrategy wakeupFallbackStrategy)
    {
        int wakeCount = wakeupFallbackStrategy == Baselib_WakeupFallbackStrategy_All || count > INT_MAX
            ? INT_MAX
            : static_cast<int>(count);
        ::syscall(__NR_futex, address, FUTEX_WAKE_PRIVATE, wakeCount, nullptr, nullptr, 0);
    }
#endif

    Baselib_SystemSemaphore_Handle Baselib_SystemSemaphore_CreateInplace(void*)
    {
        Baselib_SystemSemaphore_Handle result = {};
#if defined(_WIN32)
        result.handle = ::CreateSemaphoreW(nullptr, 0, LONG_MAX, nullptr);
        if (!result.handle)
        {
            std::abort();
        }
#else
        sem_t* semaphore = new sem_t;
        if (::sem_init(semaphore, 0, 0) != 0)
        {
            delete semaphore;
            std::abort();
        }
        result.handle = semaphore;
#endif
        return result;
    }

    void Baselib_SystemSemaphore_Acquire(Baselib_SystemSemaphore_Handle semaphore)
    {
#if defined(_WIN32)
        if (::WaitForSingleObject((HANDLE)semaphore.handle, INFINITE) != WAIT_OBJECT_0)
#else
        int result;
        do
        {
            result = ::sem_wait(static_cast<sem_t*>(semaphore.handle));
        } while (result != 0 && errno == EINTR);
        if (result != 0)
#endif
        {
            std::abort();
        }
    }

    void Baselib_SystemSemaphore_Release(Baselib_SystemSemaphore_Handle semaphore, uint32_t count)
    {
#if defined(_WIN32)
        if (!::ReleaseSemaphore((HANDLE)semaphore.handle, (LONG)count, nullptr))
        {
            std::abort();
        }
#else
        for (uint32_t i = 0; i < count; i++)
        {
            if (::sem_post(static_cast<sem_t*>(semaphore.handle)) != 0)
            {
                std::abort();
            }
        }
#endif
    }

    void Baselib_SystemSemaphore_FreeInplace(Baselib_SystemSemaphore_Handle semaphore)
    {
#if defined(_WIN32)
        ::CloseHandle((HANDLE)semaphore.handle);
#else
        sem_t* nativeSemaphore = static_cast<sem_t*>(semaphore.handle);
        ::sem_destroy(nativeSemaphore);
        delete nativeSemaphore;
#endif
    }

    Baselib_Thread_Id Baselib_Thread_GetCurrentThreadId()
    {
#if defined(_WIN32)
        return (Baselib_Thread_Id)::GetCurrentThreadId();
#else
        return static_cast<Baselib_Thread_Id>(::syscall(__NR_gettid));
#endif
    }
}

namespace
{
    std::atomic<bool> s_aotMetadataAvailable{ false };
	std::mutex s_aotMetadataLock;
	std::mutex s_aotMetadataQueryControlLock;
	std::condition_variable s_aotMetadataQueryControlChanged;
	uint64_t s_aotMetadataQueryAttemptCount = 0;
	bool s_pauseNextAotMetadataQuery = false;
	bool s_aotMetadataQueryPaused = false;
	bool s_resumeAotMetadataQuery = false;
	volatile int32_t s_interpreterStubCallCount = 0;
	struct DheResolverRecord
	{
		Il2CppAssembly* assembly;
		Il2CppImage* image;
		Il2CppClass* klass;
	};
	std::vector<DheResolverRecord> s_dheResolvers;

    void InterpreterMethodPointerStub()
    {
		++s_interpreterStubCallCount;
    }

#if HYBRIDCLR_UNITY_2021_OR_NEW
    void InterpreterInvokerStub(Il2CppMethodPointer, const MethodInfo*, void*, void**, void*)
    {
		++s_interpreterStubCallCount;
    }
#else
    void* InterpreterInvokerStub(Il2CppMethodPointer, const MethodInfo*, void*, void**)
    {
		++s_interpreterStubCallCount;
        return nullptr;
    }
#endif
}

namespace il2cpp
{
namespace utils
{
#if defined(HYBRIDCLR_TUANJIE_VERSION)
    void* Memory::Malloc(size_t size, Il2CppMemLabel)
    {
        return std::malloc(size);
    }

    void Memory::Free(void* memory, Il2CppMemLabel)
    {
        std::free(memory);
    }

    void Memory::AlignedFree(void* memory, Il2CppMemLabel)
    {
        std::free(memory);
    }
#else
    void* Memory::Malloc(size_t size)
    {
        return std::malloc(size);
    }

    void Memory::Free(void* memory)
    {
        std::free(memory);
    }

    void Memory::AlignedFree(void* memory)
    {
        std::free(memory);
    }
#endif
}

namespace vm
{
#if HYBRIDCLR_UNITY_2021_OR_NEW
    namespace
    {
        void MissingMethodInvokerStub(Il2CppMethodPointer, const MethodInfo*, void*, void**, void*)
        {
            std::abort();
        }
    }

    InvokerMethod Runtime::GetMissingMethodInvoker()
    {
        return MissingMethodInvokerStub;
    }
#endif

    void Exception::Raise(Il2CppException*, MethodInfo*)
    {
        std::abort();
    }

    Il2CppException* Exception::GetExecutionEngineException(const char*)
    {
        return nullptr;
    }

    Il2CppException* Exception::GetMissingMethodException(const char*)
    {
        return nullptr;
    }

    // The native test executable does not boot the VM. These definitions keep
    // the resolver and direct interpreter bridge linkable while their real
    // implementations remain covered by the Unity Player gate.
    Il2CppImage* Assembly::GetImage(const Il2CppAssembly* assembly)
    {
		for (const DheResolverRecord& resolver : s_dheResolvers)
		{
			if (resolver.assembly == assembly)
				return resolver.image;
		}
		return nullptr;
    }

    const Il2CppAssembly* Assembly::GetLoadedAssembly(const char*)
    {
        return nullptr;
    }

    const Il2CppAssembly* MetadataCache::GetAssemblyByName(const char* name)
    {
		for (const DheResolverRecord& resolver : s_dheResolvers)
		{
			if (resolver.assembly && name && resolver.assembly->aname.name &&
				std::strcmp(name, resolver.assembly->aname.name) == 0)
				return resolver.assembly;
		}
		return nullptr;
    }

    void Image::GetTypes(const Il2CppImage* image, bool, TypeVector* target)
    {
        if (target)
        {
            target->clear();
			for (const DheResolverRecord& resolver : s_dheResolvers)
			{
				if (image == resolver.image && resolver.klass)
				{
					target->push_back(resolver.klass);
					break;
				}
			}
        }
    }

    Il2CppClass* Image::ClassFromName(const Il2CppImage*, const char*, const char*)
    {
        return nullptr;
    }

    void Class::SetupMethods(Il2CppClass*)
    {
    }

    const MethodInfo* Class::GetMethodFromName(Il2CppClass*, const char*, int)
    {
        return nullptr;
    }
}
}

namespace il2cpp
{
namespace metadata
{
    const MethodInfo* GenericMetadata::Inflate(const MethodInfo* methodDefinition,
        const Il2CppGenericContext*)
    {
        return methodDefinition;
    }
}
}

namespace hybridclr
{
namespace interpreter
{
    void Interpreter::Execute(const MethodInfo*, StackObject*, void*)
    {
    }

    InterpMethodInfo* InterpreterModule::GetInterpMethodInfo(const MethodInfo*)
    {
        return nullptr;
    }
}
}

Il2CppRuntimeStats il2cpp_runtime_stats = {{ 0 }};
Il2CppDefaults il2cpp_defaults = {};

namespace hybridclr
{
namespace native_test
{
    void SetAOTMetadataAvailable(bool available)
    {
        s_aotMetadataAvailable.store(available, std::memory_order_release);
    }

    InvokerMethod GetInterpreterInvoker()
    {
        return InterpreterInvokerStub;
    }

    Il2CppMethodPointer GetInterpreterMethodPointer()
    {
        return InterpreterMethodPointerStub;
    }

    void PauseNextAOTMetadataQuery()
    {
		std::lock_guard<std::mutex> lock(s_aotMetadataQueryControlLock);
		s_pauseNextAotMetadataQuery = true;
		s_aotMetadataQueryPaused = false;
		s_resumeAotMetadataQuery = false;
    }

    bool WaitForPausedAOTMetadataQuery(uint32_t timeoutMilliseconds)
    {
		std::unique_lock<std::mutex> lock(s_aotMetadataQueryControlLock);
		return s_aotMetadataQueryControlChanged.wait_for(lock,
			std::chrono::milliseconds(timeoutMilliseconds), [] { return s_aotMetadataQueryPaused; });
    }

    void ResumeAOTMetadataQuery()
    {
		std::lock_guard<std::mutex> lock(s_aotMetadataQueryControlLock);
		s_resumeAotMetadataQuery = true;
		s_aotMetadataQueryControlChanged.notify_all();
    }

    uint64_t GetAOTMetadataQueryAttemptCount()
    {
		std::lock_guard<std::mutex> lock(s_aotMetadataQueryControlLock);
		return s_aotMetadataQueryAttemptCount;
    }

    bool WaitForAOTMetadataQueryAttemptAfter(uint64_t count, uint32_t timeoutMilliseconds)
    {
		std::unique_lock<std::mutex> lock(s_aotMetadataQueryControlLock);
		return s_aotMetadataQueryControlChanged.wait_for(lock,
			std::chrono::milliseconds(timeoutMilliseconds), [count]
			{
				return s_aotMetadataQueryAttemptCount > count;
			});
    }

    void AcquireAOTMetadataLock()
    {
		s_aotMetadataLock.lock();
    }

    void ReleaseAOTMetadataLock()
    {
		s_aotMetadataLock.unlock();
    }

	void ConfigureDheResolver(Il2CppAssembly* assembly, Il2CppImage* image, Il2CppClass* klass)
	{
		for (DheResolverRecord& resolver : s_dheResolvers)
		{
			if (resolver.assembly == assembly)
			{
				resolver = { assembly, image, klass };
				return;
			}
		}
		s_dheResolvers.push_back({ assembly, image, klass });
	}

	void ClearDheResolver()
	{
		s_dheResolvers.clear();
	}
}

namespace metadata
{
    AOTHomologousImage* AOTHomologousImage::FindImageByAssembly(const Il2CppAssembly*)
    {
		{
			std::lock_guard<std::mutex> controlLock(s_aotMetadataQueryControlLock);
			++s_aotMetadataQueryAttemptCount;
			s_aotMetadataQueryControlChanged.notify_all();
		}

		std::unique_lock<std::mutex> metadataLock(s_aotMetadataLock);
		bool available = s_aotMetadataAvailable.load(std::memory_order_acquire);
		std::unique_lock<std::mutex> controlLock(s_aotMetadataQueryControlLock);
		if (s_pauseNextAotMetadataQuery)
		{
			s_pauseNextAotMetadataQuery = false;
			s_aotMetadataQueryPaused = true;
			s_aotMetadataQueryControlChanged.notify_all();
			s_aotMetadataQueryControlChanged.wait(controlLock, [] { return s_resumeAotMetadataQuery; });
			s_aotMetadataQueryPaused = false;
			s_resumeAotMetadataQuery = false;
		}
		return available ? reinterpret_cast<AOTHomologousImage*>(1) : nullptr;
    }
}

namespace interpreter
{
    Il2CppMethodPointer InterpreterModule::GetMethodPointer(const MethodInfo*)
    {
        return InterpreterMethodPointerStub;
    }

    Il2CppMethodPointer InterpreterModule::GetAdjustThunkMethodPointer(const MethodInfo*)
    {
        return InterpreterMethodPointerStub;
    }

    InvokerMethod InterpreterModule::GetMethodInvoker(const MethodInfo*)
    {
        return InterpreterInvokerStub;
    }

    bool InterpreterModule::IsImplementsByInterpreter(const MethodInfo* method)
    {
        return method->invoker_method == InterpreterInvokerStub;
    }
}

namespace metadata
{
    extern const uint32_t kMetadataImageIndexExtraShiftBitsArr[4] = {
        kMetadataImageIndexExtraShiftBitsA,
        kMetadataImageIndexExtraShiftBitsB,
        kMetadataImageIndexExtraShiftBitsC,
        kMetadataImageIndexExtraShiftBitsD,
    };
    extern const uint32_t kMetadataIndexMaskArr[4] = {
        kMetadataIndexMaskA,
        kMetadataIndexMaskB,
        kMetadataIndexMaskC,
        kMetadataIndexMaskD,
    };
}
}
