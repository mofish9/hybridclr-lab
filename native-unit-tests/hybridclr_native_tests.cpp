#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <limits>
#include <string>
#include <thread>
#include <vector>

#include "hybridclr/Il2CppCompatibleDef.h"
#if __has_include("hybridclr/DheRuntime.h")
#include "hybridclr/DheRuntime.h"
#define HYBRIDCLR_LAB_DHE_ENABLED 1
#else
#define HYBRIDCLR_LAB_DHE_ENABLED 0
#endif
#include "hybridclr/metadata/BlobReader.h"
#include "hybridclr/metadata/MetadataUtil.h"
#include "hybridclr/metadata/MetadataModule.h"
#include "hybridclr/metadata/Opcodes.h"
#include "hybridclr/transform/BasicBlockSpliter.h"
#include "hybridclr/transform/TemporaryMemoryArena.h"
#include "hybridclr/interpreter/MemoryUtil.h"
#include "native_test_hooks.h"

#if __has_include("hybridclr/transform/OptimizationFacts.h")
#include "hybridclr/transform/OptimizationFacts.h"
#define HYBRIDCLR_LAB_HAS_OPTIMIZATION_FACTS 1
#else
#define HYBRIDCLR_LAB_HAS_OPTIMIZATION_FACTS 0
#endif

#if __has_include("hybridclr/transform/InstructionCombiner.h")
#include "hybridclr/transform/InstructionCombiner.h"
#define HYBRIDCLR_LAB_HAS_INSTRUCTION_COMBINER 1
#else
#define HYBRIDCLR_LAB_HAS_INSTRUCTION_COMBINER 0
#endif

namespace
{
    int g_failures = 0;

#ifndef HYBRIDCLR_LAB_FGS_TESTS
#define HYBRIDCLR_LAB_FGS_TESTS 0
#endif

    void Check(bool condition, const char* expression, const char* file, int line)
    {
        if (!condition)
        {
            std::cerr << file << ':' << line << " failed: " << expression << '\n';
            ++g_failures;
        }
    }

#define CHECK(expression) Check((expression), #expression, __FILE__, __LINE__)

    int32_t InterpreterProbeMethod(int32_t value, const MethodInfo*)
    {
        return value + 100;
    }

#if HYBRIDCLR_LAB_DHE_ENABLED
    using DheProbeMethod = int32_t(*)(int32_t, const MethodInfo*);

    // This mirrors the ABI-sensitive shape that IL2CPP must generate at the
    // top of a DHE AOT method. The production generator has to emit the exact
    // return/argument types for every method rather than use this probe type.
    int32_t GeneratedLikeDheEntry(int32_t value, const MethodInfo* method)
    {
        if (hybridclr::dhe::ShouldDispatchToInterpreter(method))
        {
            method = hybridclr::dhe::ResolveInterpreterMethod(method);
            DheProbeMethod interpreterMethod = reinterpret_cast<DheProbeMethod>(
                method->methodPointerCallByInterp);
            return interpreterMethod(value, method);
        }
        return value * 2;
    }
#endif

#if HYBRIDCLR_LAB_FGS_TESTS
    void DummyMethodPointer()
    {
    }

#if HYBRIDCLR_UNITY_2021_OR_NEW
    void DummyInvoker(Il2CppMethodPointer, const MethodInfo*, void*, void**, void*)
    {
    }
#endif

    void TestManagedToNativeCallSelection()
    {
        MethodInfo method{};
        method.methodPointerCallByInterp = DummyMethodPointer;

        CHECK(!hybridclr::IsFullGenericSharingMethod(&method));
        CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&method));
        CHECK(hybridclr::GetInterpreterInvokerMethodPointer(&method) == DummyMethodPointer);

#if HYBRIDCLR_UNITY_2021_OR_NEW
		Il2CppAssembly assembly{};
		Il2CppImage image{};
		Il2CppClass* klass = static_cast<Il2CppClass*>(std::calloc(1, sizeof(Il2CppClass)));
		CHECK(klass != nullptr);
		if (!klass)
		{
			return;
		}
		image.assembly = &assembly;
		klass->image = &image;
		klass->parent = klass;

        MethodInfo fgsMethod{};
        fgsMethod.has_full_generic_sharing_signature = true;
		fgsMethod.hasFullGenericSharingAotInvoker = true;
        fgsMethod.methodPointerCallByInterp = DummyMethodPointer;
        fgsMethod.methodPointer = DummyMethodPointer;
		fgsMethod.invoker_method = DummyInvoker;
		fgsMethod.klass = klass;
		fgsMethod.name = "PreInflatedFGSMethod";
		CHECK(hybridclr::NormalizeFullGenericSharingAotInvoker(nullptr) ==
			il2cpp::vm::Runtime::GetMissingMethodInvoker());
		CHECK(!hybridclr::IsValidFullGenericSharingAotInvoker(
			hybridclr::NormalizeFullGenericSharingAotInvoker(nullptr)));
		CHECK(hybridclr::NormalizeFullGenericSharingAotInvoker(DummyInvoker) == DummyInvoker);
		CHECK(hybridclr::IsValidFullGenericSharingAotInvoker(DummyInvoker));

		hybridclr::native_test::SetAOTMetadataAvailable(false);
        CHECK(hybridclr::IsFullGenericSharingMethod(&fgsMethod));
        CHECK(!fgsMethod.initInterpCallMethodPointer);
        CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&fgsMethod));
		CHECK(!fgsMethod.initInterpCallMethodPointer);
		CHECK(fgsMethod.invoker_method == DummyInvoker);

		// Preparation is immutable. Loading metadata after first use must not
		// rewrite an already published AOT MethodInfo or cached delegate pointer.
		uint32_t preparedAotState = fgsMethod.fullGenericSharingPreparationState;
		CHECK(preparedAotState != 0);
		hybridclr::native_test::SetAOTMetadataAvailable(true);
		hybridclr::NotifyAOTMetadataLoaded();
		CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&fgsMethod));
		CHECK(fgsMethod.fullGenericSharingPreparationState == preparedAotState);
		CHECK(!fgsMethod.initInterpCallMethodPointer);
		CHECK(!fgsMethod.isInterpterImpl);
		CHECK(fgsMethod.invoker_method == DummyInvoker);
		CHECK(hybridclr::GetInterpreterInvokerMethodPointer(&fgsMethod) == DummyMethodPointer);

		// Supplemental metadata must not downgrade a valid native FGS invoker.
		MethodInfo preloadedAotMethod{};
		preloadedAotMethod.has_full_generic_sharing_signature = true;
		preloadedAotMethod.hasFullGenericSharingAotInvoker = true;
		preloadedAotMethod.methodPointerCallByInterp = DummyMethodPointer;
		preloadedAotMethod.methodPointer = DummyMethodPointer;
		preloadedAotMethod.invoker_method = DummyInvoker;
		preloadedAotMethod.klass = klass;
		preloadedAotMethod.name = "PreloadedAOTFGSMethod";
		CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&preloadedAotMethod));
		CHECK(!preloadedAotMethod.isInterpterImpl);
		CHECK(preloadedAotMethod.invoker_method == DummyInvoker);

		// A missing raw AOT invoker may use the interpreter only when metadata is
		// available before this MethodInfo's first preparation.
		MethodInfo missingAotMethod{};
		missingAotMethod.has_full_generic_sharing_signature = true;
		missingAotMethod.hasFullGenericSharingAotInvoker = false;
		missingAotMethod.methodPointerCallByInterp = DummyMethodPointer;
		missingAotMethod.methodPointer = DummyMethodPointer;
		missingAotMethod.invoker_method = DummyInvoker;
		missingAotMethod.klass = klass;
		missingAotMethod.name = "MissingAOTFGSMethod";
		CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&missingAotMethod));
		CHECK(missingAotMethod.initInterpCallMethodPointer);
		CHECK(missingAotMethod.isInterpterImpl);
		CHECK(missingAotMethod.invoker_method == hybridclr::native_test::GetInterpreterInvoker());
		CHECK(hybridclr::GetInterpreterInvokerMethodPointer(&missingAotMethod) ==
			hybridclr::native_test::GetInterpreterMethodPointer());

		MethodInfo concurrentMethod{};
		concurrentMethod.has_full_generic_sharing_signature = true;
		concurrentMethod.hasFullGenericSharingAotInvoker = false;
		concurrentMethod.methodPointerCallByInterp = DummyMethodPointer;
		concurrentMethod.methodPointer = DummyMethodPointer;
		concurrentMethod.invoker_method = DummyInvoker;
		concurrentMethod.klass = klass;
		concurrentMethod.name = "ConcurrentFGSMethod";
		std::atomic<int32_t> concurrentFailures{ 0 };
		std::array<std::thread, 8> workers;
		for (std::thread& worker : workers)
		{
			worker = std::thread([&]()
			{
				if (!hybridclr::PrepareInterpreterManaged2NativeCall(&concurrentMethod))
				{
					concurrentFailures.fetch_add(1, std::memory_order_relaxed);
				}
			});
		}
		for (std::thread& worker : workers)
		{
			worker.join();
		}
		CHECK(concurrentFailures.load(std::memory_order_relaxed) == 0);
		CHECK(concurrentMethod.initInterpCallMethodPointer);
		CHECK(concurrentMethod.isInterpterImpl);
		CHECK(concurrentMethod.invoker_method == hybridclr::native_test::GetInterpreterInvoker());
		CHECK(hybridclr::GetInterpreterInvokerMethodPointer(&concurrentMethod) ==
			hybridclr::native_test::GetInterpreterMethodPointer());

		// Pause after the first metadata lookup has observed "unavailable". A
		// concurrent registration changes the epoch before preparation can publish,
		// so the method must retry the lookup and select the interpreter bridge.
		MethodInfo metadataRaceMethod{};
		metadataRaceMethod.has_full_generic_sharing_signature = true;
		metadataRaceMethod.hasFullGenericSharingAotInvoker = false;
		metadataRaceMethod.methodPointerCallByInterp = DummyMethodPointer;
		metadataRaceMethod.methodPointer = DummyMethodPointer;
		metadataRaceMethod.invoker_method = il2cpp::vm::Runtime::GetMissingMethodInvoker();
		metadataRaceMethod.klass = klass;
		metadataRaceMethod.name = "ConcurrentMetadataRegistrationFGSMethod";
		hybridclr::native_test::SetAOTMetadataAvailable(false);
		hybridclr::native_test::PauseNextAOTMetadataQuery();
		bool metadataRacePrepared = false;
		std::thread metadataRaceWorker([&]
		{
			metadataRacePrepared = hybridclr::PrepareInterpreterManaged2NativeCall(&metadataRaceMethod);
		});
		bool metadataQueryPaused = hybridclr::native_test::WaitForPausedAOTMetadataQuery(5000);
		CHECK(metadataQueryPaused);
		if (metadataQueryPaused)
		{
			hybridclr::native_test::SetAOTMetadataAvailable(true);
			hybridclr::NotifyAOTMetadataLoaded();
		}
		hybridclr::native_test::ResumeAOTMetadataQuery();
		metadataRaceWorker.join();
		CHECK(metadataRacePrepared);
		CHECK(metadataRaceMethod.isInterpterImpl);
		CHECK(metadataRaceMethod.invoker_method == hybridclr::native_test::GetInterpreterInvoker());

		// Reproduce the former g_MetadataLock -> method-pointer lock edge. The
		// preparation thread must not own the method-pointer lock while its metadata
		// query is blocked, otherwise CopyMethodInfo deadlocks here.
		MethodInfo lockOrderMethod{};
		lockOrderMethod.has_full_generic_sharing_signature = true;
		lockOrderMethod.hasFullGenericSharingAotInvoker = false;
		lockOrderMethod.methodPointerCallByInterp = DummyMethodPointer;
		lockOrderMethod.methodPointer = DummyMethodPointer;
		lockOrderMethod.invoker_method = il2cpp::vm::Runtime::GetMissingMethodInvoker();
		lockOrderMethod.klass = klass;
		lockOrderMethod.name = "MetadataLockOrderFGSMethod";
		MethodInfo lockOrderCopy{};
		uint64_t queryCount = hybridclr::native_test::GetAOTMetadataQueryAttemptCount();
		hybridclr::native_test::AcquireAOTMetadataLock();
		bool lockOrderPrepared = false;
		std::thread lockOrderWorker([&]
		{
			lockOrderPrepared = hybridclr::PrepareInterpreterManaged2NativeCall(&lockOrderMethod);
		});
		bool metadataQueryAttempted = hybridclr::native_test::WaitForAOTMetadataQueryAttemptAfter(queryCount, 5000);
		CHECK(metadataQueryAttempted);
		if (metadataQueryAttempted)
		{
			hybridclr::CopyMethodInfo(&lockOrderCopy, &lockOrderMethod, sizeof(MethodInfo));
		}
		hybridclr::native_test::ReleaseAOTMetadataLock();
		lockOrderWorker.join();
		CHECK(lockOrderPrepared);

		MethodInfo lateMetadataMethod{};
		lateMetadataMethod.has_full_generic_sharing_signature = true;
		lateMetadataMethod.hasFullGenericSharingAotInvoker = false;
		lateMetadataMethod.methodPointerCallByInterp = DummyMethodPointer;
		lateMetadataMethod.methodPointer = DummyMethodPointer;
		lateMetadataMethod.invoker_method = DummyInvoker;
		lateMetadataMethod.klass = klass;
		lateMetadataMethod.name = "LateMetadataFGSMethod";
		hybridclr::native_test::SetAOTMetadataAvailable(false);
		hybridclr::NotifyAOTMetadataLoaded();
		CHECK(!hybridclr::PrepareInterpreterManaged2NativeCall(&lateMetadataMethod));
		uint32_t missingAotState = lateMetadataMethod.fullGenericSharingPreparationState;
		CHECK(missingAotState != 0);
		hybridclr::native_test::SetAOTMetadataAvailable(true);
		hybridclr::NotifyAOTMetadataLoaded();
		CHECK(!hybridclr::PrepareInterpreterManaged2NativeCall(&lateMetadataMethod));
		CHECK(lateMetadataMethod.fullGenericSharingPreparationState == missingAotState);
		CHECK(!lateMetadataMethod.isInterpterImpl);
		CHECK(lateMetadataMethod.invoker_method == DummyInvoker);

        MethodInfo nullPointerMethod{};
        nullPointerMethod.has_full_generic_sharing_signature = true;
		nullPointerMethod.hasFullGenericSharingAotInvoker = true;
        nullPointerMethod.methodPointerCallByInterp = DummyMethodPointer;
        nullPointerMethod.methodPointer = nullptr;
        nullPointerMethod.invoker_method = DummyInvoker;
		nullPointerMethod.klass = klass;
		nullPointerMethod.name = "AOTMethodWithNullDirectPointer";
		hybridclr::native_test::SetAOTMetadataAvailable(false);
		hybridclr::NotifyAOTMetadataLoaded();
        CHECK(hybridclr::PrepareInterpreterManaged2NativeCall(&nullPointerMethod));
		CHECK(!nullPointerMethod.initInterpCallMethodPointer);
        CHECK(hybridclr::GetInterpreterInvokerMethodPointer(&nullPointerMethod) == nullptr);

		hybridclr::native_test::SetAOTMetadataAvailable(false);
		hybridclr::NotifyAOTMetadataLoaded();
        MethodInfo missingInvokerMethod{};
        missingInvokerMethod.has_full_generic_sharing_signature = true;
		missingInvokerMethod.hasFullGenericSharingAotInvoker = false;
        missingInvokerMethod.methodPointerCallByInterp = DummyMethodPointer;
        missingInvokerMethod.invoker_method = il2cpp::vm::Runtime::GetMissingMethodInvoker();
		missingInvokerMethod.klass = klass;
		missingInvokerMethod.name = "MissingFGSMethod";
        CHECK(!hybridclr::PrepareInterpreterManaged2NativeCall(&missingInvokerMethod));
		CHECK(!missingInvokerMethod.initInterpCallMethodPointer);

        MethodInfo nullInvokerMethod{};
        nullInvokerMethod.has_full_generic_sharing_signature = true;
		nullInvokerMethod.hasFullGenericSharingAotInvoker = false;
        nullInvokerMethod.methodPointerCallByInterp = DummyMethodPointer;
		nullInvokerMethod.invoker_method = hybridclr::NormalizeFullGenericSharingAotInvoker(nullptr);
		nullInvokerMethod.klass = klass;
		nullInvokerMethod.name = "NullInvokerFGSMethod";
		CHECK(!hybridclr::PrepareInterpreterManaged2NativeCall(&nullInvokerMethod));
		CHECK(!nullInvokerMethod.initInterpCallMethodPointer);
		CHECK(nullInvokerMethod.invoker_method == il2cpp::vm::Runtime::GetMissingMethodInvoker());

		MethodInfo copySource{};
		copySource.has_full_generic_sharing_signature = true;
		copySource.hasFullGenericSharingAotInvoker = true;
		copySource.methodPointer = DummyMethodPointer;
		copySource.invoker_method = DummyInvoker;
		copySource.fullGenericSharingPreparationState = std::numeric_limits<uint32_t>::max();
		MethodInfo copyDestination{};
		hybridclr::CopyMethodInfo(&copyDestination, &copySource, sizeof(MethodInfo));
		CHECK(copyDestination.fullGenericSharingPreparationState == 0);
		CHECK(copyDestination.hasFullGenericSharingAotInvoker);
		CHECK(copyDestination.methodPointer == DummyMethodPointer);
		CHECK(copyDestination.invoker_method == DummyInvoker);
		std::free(klass);
#endif
    }
#endif

    void TestBlobReader()
    {
        const hybridclr::byte bytes[] = {
            0x7F,
            0x80, 0x80,
            0xC0, 0x00, 0x01, 0x02,
            0x34, 0x12,
            0x78, 0x56, 0x34, 0x12,
            0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        };
        hybridclr::metadata::BlobReader reader(bytes, sizeof(bytes));
        CHECK(reader.ReadCompressedUint32() == 0x7F);
        CHECK(reader.ReadCompressedUint32() == 0x80);
        CHECK(reader.ReadCompressedUint32() == 0x000102);
        CHECK(reader.Read16() == 0x1234);
        CHECK(reader.Read32() == 0x12345678);
        CHECK(reader.Read64() == 0x0123456789ABCDEFULL);
        CHECK(reader.IsEmpty());

        const hybridclr::byte signedBytes[] = { 0x00, 0x7F, 0x01, 0x80, 0x81 };
        hybridclr::metadata::BlobReader signedReader(signedBytes, sizeof(signedBytes));
        CHECK(signedReader.ReadCompressedInt32() == 0);
        CHECK(signedReader.ReadCompressedInt32() == -1);
        CHECK(signedReader.ReadCompressedInt32() == -64);
        CHECK(signedReader.ReadCompressedInt32() == -8128);

        const hybridclr::byte boundaryBytes[] = {
            0x7F,
            0xBF, 0xFF,
            0xDF, 0xFF, 0xFF, 0xFF,
        };
        hybridclr::metadata::BlobReader boundaryReader(boundaryBytes, sizeof(boundaryBytes));
        CHECK(boundaryReader.ReadCompressedUint32() == 0x7F);
        CHECK(boundaryReader.ReadCompressedUint32() == 0x3FFF);
        CHECK(boundaryReader.ReadCompressedUint32() == 0x1FFFFFFF);
        CHECK(boundaryReader.IsEmpty());

        const hybridclr::byte floatingBytes[] = {
            0x00, 0x00, 0x80, 0x3F,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F,
        };
        hybridclr::metadata::BlobReader floatingReader(floatingBytes, sizeof(floatingBytes));
        CHECK(floatingReader.ReadFloat() == 1.0f);
        CHECK(floatingReader.ReadDouble() == 1.0);

        const hybridclr::byte tryReadBytes[] = { 1, 2, 3, 4, 5, 6 };
        hybridclr::metadata::BlobReader tryReader(tryReadBytes, sizeof(tryReadBytes));
        uint32_t value = 0;
        CHECK(tryReader.TryRead32(value));
        CHECK(value == 0x04030201);
        CHECK(tryReader.GetReadPosition() == 4);
        CHECK(!tryReader.TryRead32(value));
        CHECK(tryReader.GetReadPosition() == 4);
        CHECK(tryReader.PeekByte() == 5);
        tryReader.SkipByte();
        const hybridclr::byte* tail = tryReader.GetAndSkipCurBytes(1);
        CHECK(*tail == 6);
        CHECK(tryReader.IsEmpty());
    }

    void TestMetadataUtilities()
    {
#if HYBRIDCLR_LAB_HAS_PREPARATION_STATE
		static_assert(offsetof(MethodInfo, fullGenericSharingPreparationState) >
			offsetof(MethodInfo, virtualMethodPointerCallByInterp),
			"The state field must be appended after the existing HybridCLR method pointers.");
		CHECK(sizeof(MethodInfo) >= offsetof(MethodInfo, fullGenericSharingPreparationState) + sizeof(uint32_t));
#endif
        const hybridclr::byte bytes[] = {
            0x34, 0x12,
            0x78, 0x56, 0x34, 0x12,
            0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,
        };
        CHECK(hybridclr::metadata::GetU2LittleEndian(bytes) == 0x1234);
        CHECK(hybridclr::metadata::GetU4LittleEndian(bytes + 2) == 0x12345678);
        CHECK(hybridclr::metadata::GetU8LittleEndian(bytes + 6) == 0x0123456789ABCDEFULL);
        CHECK(hybridclr::metadata::GetI2LittleEndian(bytes) == 0x1234);
        CHECK(hybridclr::metadata::GetI4LittleEndian(bytes + 2) == 0x12345678);
        CHECK(hybridclr::metadata::GetI8LittleEndian(bytes + 6) == 0x0123456789ABCDEFLL);

        const int32_t encoded = hybridclr::metadata::EncodeImageAndMetadataIndex(64, 0x12345);
        CHECK(encoded != hybridclr::metadata::kInvalidIndex);
        CHECK(hybridclr::metadata::DecodeImageIndex(encoded) == 64);
        CHECK(hybridclr::metadata::DecodeMetadataIndex(encoded) == 0x12345);
        CHECK(hybridclr::metadata::IsInterpreterIndex(encoded));
        CHECK(hybridclr::metadata::DecodeImageIndex(hybridclr::metadata::kInvalidIndex) == 0);
        CHECK(hybridclr::metadata::DecodeMetadataIndex(hybridclr::metadata::kInvalidIndex) == hybridclr::metadata::kInvalidIndex);
        CHECK(!hybridclr::metadata::IsInterpreterIndex(hybridclr::metadata::kInvalidIndex));
    }

#if HYBRIDCLR_LAB_DHE_ENABLED
    void TestDheMethodRegistry()
    {
        using DheI4I4 = int32_t(*)(const MethodInfo*, int32_t);
        using DheI4I4I4 = int32_t(*)(const MethodInfo*, int32_t, int32_t);
        using DheI8I8 = int64_t(*)(const MethodInfo*, int64_t);
        using DheVoidI4 = void(*)(const MethodInfo*, int32_t);
        using DheInstanceI4I4 = int32_t(*)(const MethodInfo*, void*, int32_t);
        using DheInstanceI8I8 = int64_t(*)(const MethodInfo*, void*, int64_t);
        using DheInstanceVoidI4 = void(*)(const MethodInfo*, void*, int32_t);
        using DheValueTypeInstanceVoidNoArgs = void(*)(const MethodInfo*, void*);
        using DheInvokeArgs = void(*)(const MethodInfo*, void*, void**, const uint8_t*, uint32_t, void*);
        // Keep every generated Player ABI shape link-checked in the native
        // gate even though the VM-backed execution is covered by Unity.
        DheI4I4 i4i4 = &hybridclr::dhe::ExecuteInterpreterI4I4;
        DheI4I4I4 i4i4i4 = &hybridclr::dhe::ExecuteInterpreterI4I4I4;
        DheI8I8 i8i8 = &hybridclr::dhe::ExecuteInterpreterI8I8;
        DheVoidI4 voidi4 = &hybridclr::dhe::ExecuteInterpreterVoidI4;
        DheInstanceI4I4 instanceI4i4 = &hybridclr::dhe::ExecuteInterpreterInstanceI4I4;
        DheInstanceI8I8 instanceI8i8 = &hybridclr::dhe::ExecuteInterpreterInstanceI8I8;
        DheInstanceVoidI4 instanceVoidi4 = &hybridclr::dhe::ExecuteInterpreterInstanceVoidI4;
        DheValueTypeInstanceVoidNoArgs valueTypeInstanceVoidNoArgs = &hybridclr::dhe::ExecuteInterpreterValueTypeInstanceVoidNoArgs;
        DheInvokeArgs invokeArgs = &hybridclr::dhe::ExecuteInterpreterInvokeArgs;
        CHECK(i4i4 != nullptr && i4i4i4 != nullptr && i8i8 != nullptr && voidi4 != nullptr &&
            instanceI4i4 != nullptr && instanceI8i8 != nullptr && instanceVoidi4 != nullptr &&
            valueTypeInstanceVoidNoArgs != nullptr && invokeArgs != nullptr);

        const std::string assemblyName = "Test.Assembly";
        std::vector<uint8_t> mvBytes;
        const auto appendU32 = [&mvBytes](uint32_t value)
        {
            mvBytes.push_back(static_cast<uint8_t>(value & 0xff));
            mvBytes.push_back(static_cast<uint8_t>((value >> 8) & 0xff));
            mvBytes.push_back(static_cast<uint8_t>((value >> 16) & 0xff));
            mvBytes.push_back(static_cast<uint8_t>((value >> 24) & 0xff));
        };
        const auto appendDigest = [&mvBytes](uint8_t seed)
        {
            for (uint32_t index = 0; index < hybridclr::dhe::kSha256DigestSize; ++index)
            {
                mvBytes.push_back(static_cast<uint8_t>(seed + index));
            }
        };
        const char magic[] = "DHEMETA1";
        mvBytes.insert(mvBytes.end(), magic, magic + 8);
        appendU32(hybridclr::dhe::kMetaVersionSchema);
        appendU32(hybridclr::dhe::kMetaVersionStrictCompatibilityFlag);
        appendU32(static_cast<uint32_t>(assemblyName.size()));
        appendU32(1);
        appendU32(1);
        appendDigest(1);
        mvBytes.insert(mvBytes.end(), assemblyName.begin(), assemblyName.end());
        appendDigest(33);
        appendDigest(65);
        appendU32(0x02000002);
        appendU32(0);
        appendDigest(97);
        appendDigest(129);
        appendDigest(33);
        appendU32(0x06000002);
        appendU32(8);

        hybridclr::dhe::MetaVersionData parsedMv;
        CHECK(hybridclr::dhe::ParseMetaVersion(mvBytes.data(),
            static_cast<uint32_t>(mvBytes.size()), parsedMv));
        CHECK(parsedMv.assemblyName == assemblyName);
        CHECK(parsedMv.types.size() == 1);
        CHECK(parsedMv.methods.size() == 1);
        CHECK(parsedMv.types[0].token == 0x02000002);
        CHECK(parsedMv.methods[0].token == 0x06000002);
        CHECK(parsedMv.methods[0].declaringTypeStableId == parsedMv.types[0].stableId);
        std::vector<uint8_t> badMv = mvBytes;
        badMv[8] = 2;
        hybridclr::dhe::MetaVersionData rejectedMv;
        CHECK(!hybridclr::dhe::ParseMetaVersion(badMv.data(),
            static_cast<uint32_t>(badMv.size()), rejectedMv));

        const size_t methodStart = 60 + assemblyName.size() + 72;
        std::vector<uint8_t> duplicateTokenMv = mvBytes;
        duplicateTokenMv[24] = 2;
        duplicateTokenMv.insert(duplicateTokenMv.end(), mvBytes.begin() + methodStart,
            mvBytes.end());
        duplicateTokenMv[mvBytes.size()] ^= 0x55;
        CHECK(!hybridclr::dhe::ParseMetaVersion(duplicateTokenMv.data(),
            static_cast<uint32_t>(duplicateTokenMv.size()), rejectedMv));

        // Unknown MV feature bits must fail closed. A future producer cannot
        // silently opt an older runtime into an incompatible wire format.
        mvBytes[12] = 0x02;
        hybridclr::dhe::MetaVersionData unknownFlagsMv;
        CHECK(!hybridclr::dhe::ParseMetaVersion(mvBytes.data(), static_cast<uint32_t>(mvBytes.size()), unknownFlagsMv));
        mvBytes[12] = static_cast<uint8_t>(hybridclr::dhe::kMetaVersionStrictCompatibilityFlag);

        hybridclr::dhe::Sha256Digest abcHash{};
        const char abc[] = "abc";
        CHECK(hybridclr::dhe::ComputeSha256(abc, 3, abcHash));
        const uint8_t expectedAbcHash[hybridclr::dhe::kSha256DigestSize] = {
            0xba, 0x78, 0x16, 0xbf, 0x8f, 0x01, 0xcf, 0xea,
            0x41, 0x41, 0x40, 0xde, 0x5d, 0xae, 0x22, 0x23,
            0xb0, 0x03, 0x61, 0xa3, 0x96, 0x17, 0x7a, 0x9c,
            0xb4, 0x10, 0xff, 0x61, 0xf2, 0x00, 0x15, 0xad,
        };
        CHECK(std::memcmp(abcHash.data(), expectedAbcHash, sizeof(expectedAbcHash)) == 0);

        hybridclr::dhe::ResetForTests();

        Il2CppAssembly assembly{};
        Il2CppImage image{};
        Il2CppClass* klass = static_cast<Il2CppClass*>(std::calloc(1, sizeof(Il2CppClass)));
        CHECK(klass != nullptr);
        if (!klass)
        {
            return;
        }
        image.assembly = &assembly;
        klass->image = &image;
		assembly.aname.name = "DheNativeResolver";
		assembly.image = &image;
		klass->name = "DheNativeType";
		klass->namespaze = "";

        MethodInfo changed{};
        changed.klass = klass;
        changed.token = 0x06000002;
        MethodInfo unchanged{};
        unchanged.klass = klass;
        unchanged.token = 0x06000003;
        // The current image is allowed to assign different method tokens. In
        // particular, exercise both directions of a token collision: a
        // changed current method carries the unchanged Base token, while an
        // unchanged current method carries the changed Base token.
        MethodInfo reorderedCurrentChanged{};
        reorderedCurrentChanged.klass = klass;
        reorderedCurrentChanged.token = unchanged.token;
        MethodInfo reorderedCurrentUnchanged{};
        reorderedCurrentUnchanged.klass = klass;
        reorderedCurrentUnchanged.token = changed.token;
        changed.methodPointerCallByInterp = reinterpret_cast<Il2CppMethodPointer>(InterpreterProbeMethod);
        unchanged.methodPointerCallByInterp = reinterpret_cast<Il2CppMethodPointer>(InterpreterProbeMethod);
		const MethodInfo* resolverMethods[] = { &changed, &unchanged };
		klass->methods = resolverMethods;
		klass->method_count = 2;
		hybridclr::native_test::ConfigureDheResolver(&assembly, &image, klass);

		// Exercise the production transaction entry point. It must resolve and
		// prepare all changed methods before publishing the assembly state.
		klass->token = 0x02000002;
		hybridclr::dhe::MetaVersionData baseMetaVersion;
		hybridclr::dhe::MetaVersionData currentMetaVersion;
		baseMetaVersion.assemblyName = assembly.aname.name;
		currentMetaVersion.assemblyName = assembly.aname.name;
		hybridclr::dhe::MetaVersionType baseType;
		baseType.stableId.fill(1);
		baseType.version.fill(2);
		baseType.token = klass->token;
		baseMetaVersion.types.push_back(baseType);
		currentMetaVersion.types.push_back(baseType);
		hybridclr::dhe::MetaVersionMethod baseMethod;
		baseMethod.stableId.fill(3);
		baseMethod.version.fill(4);
		baseMethod.declaringTypeStableId = baseType.stableId;
		baseMethod.token = changed.token;
		baseMethod.flags = 8;
		hybridclr::dhe::MetaVersionMethod currentMethod = baseMethod;
		currentMethod.version.fill(5);
        baseMetaVersion.methods.push_back(baseMethod);
        currentMetaVersion.methods.push_back(currentMethod);
        CHECK(hybridclr::dhe::RegisterLogicalMethodMapping(&assembly,
            &reorderedCurrentChanged, &changed));
        CHECK(hybridclr::dhe::RegisterLogicalMethodMapping(&assembly,
            &reorderedCurrentUnchanged, &unchanged));
        CHECK(hybridclr::dhe::PrepareAndRegisterMetaVersion(&assembly,
            baseMetaVersion, currentMetaVersion));
		CHECK(changed.isInterpterImpl);
		CHECK(hybridclr::dhe::IsDheAssembly(&assembly));
        CHECK(hybridclr::dhe::IsChangedMethod(&changed));
        CHECK(hybridclr::dhe::IsChangedMethod(&reorderedCurrentChanged));
        CHECK(!hybridclr::dhe::IsChangedMethod(&reorderedCurrentUnchanged));
		CHECK(!hybridclr::dhe::PrepareAndRegisterMetaVersion(&assembly,
			baseMetaVersion, currentMetaVersion));
        CHECK(!hybridclr::dhe::IsChangedMethod(&unchanged));
        CHECK(hybridclr::dhe::ShouldDispatchToInterpreter(&changed));
        CHECK(!hybridclr::dhe::ShouldDispatchToInterpreter(&unchanged));

        std::atomic<int> lookupFailures{ 0 };
        std::vector<std::thread> lookupThreads;
        for (int threadIndex = 0; threadIndex < 4; ++threadIndex)
        {
            lookupThreads.emplace_back([&changed, &unchanged, &lookupFailures]() {
                for (int iteration = 0; iteration < 10000; ++iteration)
                {
                    if (!hybridclr::dhe::IsChangedMethod(&changed) ||
                        hybridclr::dhe::IsChangedMethod(&unchanged))
                    {
                        lookupFailures.fetch_add(1, std::memory_order_relaxed);
                        return;
                    }
                }
            });
        }
        for (std::thread& thread : lookupThreads)
        {
            thread.join();
        }
        CHECK(lookupFailures.load(std::memory_order_relaxed) == 0);
        CHECK(GeneratedLikeDheEntry(1, &changed) == 101);
        CHECK(GeneratedLikeDheEntry(2, &unchanged) == 4);

        // The metadata module keeps ordinary supplementary metadata assembly-wide,
        // but narrows an explicitly registered DHE assembly to changed methods.
        hybridclr::native_test::SetAOTMetadataAvailable(true);
        CHECK(hybridclr::metadata::MetadataModule::IsImplementedByInterpreter(&changed));
        CHECK(!hybridclr::metadata::MetadataModule::IsImplementedByInterpreter(&unchanged));
        hybridclr::native_test::SetAOTMetadataAvailable(false);

		// A later unresolved token must roll back preparation of an earlier token
		// and leave the assembly unpublished.
		hybridclr::dhe::ResetForTests();
		changed.isInterpterImpl = false;
		const auto previousChangedPointer = changed.methodPointerCallByInterp;
		hybridclr::dhe::MetaVersionMethod invalidBaseMethod = baseMethod;
		invalidBaseMethod.stableId.fill(6);
		invalidBaseMethod.token = 0x06000099;
		hybridclr::dhe::MetaVersionMethod invalidCurrentMethod = invalidBaseMethod;
		invalidCurrentMethod.version.fill(7);
		baseMetaVersion.methods.push_back(invalidBaseMethod);
		currentMetaVersion.methods.push_back(invalidCurrentMethod);
		CHECK(!hybridclr::dhe::PrepareAndRegisterMetaVersion(&assembly,
			baseMetaVersion, currentMetaVersion));
		CHECK(!hybridclr::dhe::IsDheAssembly(&assembly));
		CHECK(!changed.isInterpterImpl);
		CHECK(changed.methodPointerCallByInterp == previousChangedPointer);

		// A complete hotfix set is one dispatch transaction. An invalid method
		// in the second assembly must leave the first assembly unpublished; a
		// retry with both valid registrations publishes both together.
		baseMetaVersion.methods.resize(1);
		currentMetaVersion.methods.resize(1);
		Il2CppAssembly secondAssembly{};
		Il2CppImage secondImage{};
		Il2CppClass* secondKlass =
			static_cast<Il2CppClass*>(std::calloc(1, sizeof(Il2CppClass)));
		CHECK(secondKlass != nullptr);
		if (!secondKlass)
		{
			hybridclr::native_test::ClearDheResolver();
			std::free(klass);
			return;
		}
		secondAssembly.aname.name = "DheNativeResolver.Second";
		secondAssembly.image = &secondImage;
		secondImage.assembly = &secondAssembly;
		secondKlass->image = &secondImage;
		secondKlass->name = "DheNativeTypeSecond";
		secondKlass->namespaze = "";
		secondKlass->token = 0x02000002;
		MethodInfo secondChanged{};
		secondChanged.klass = secondKlass;
		secondChanged.token = 0x06000002;
		secondChanged.methodPointerCallByInterp =
			reinterpret_cast<Il2CppMethodPointer>(InterpreterProbeMethod);
		const MethodInfo* secondMethods[] = { &secondChanged };
		secondKlass->methods = secondMethods;
		secondKlass->method_count = 1;
		hybridclr::native_test::ConfigureDheResolver(
			&secondAssembly, &secondImage, secondKlass);

		hybridclr::dhe::MetaVersionData secondBaseMetaVersion;
		hybridclr::dhe::MetaVersionData secondCurrentMetaVersion;
		secondBaseMetaVersion.assemblyName = secondAssembly.aname.name;
		secondCurrentMetaVersion.assemblyName = secondAssembly.aname.name;
		hybridclr::dhe::MetaVersionType secondType = baseType;
		secondBaseMetaVersion.types.push_back(secondType);
		secondCurrentMetaVersion.types.push_back(secondType);
		hybridclr::dhe::MetaVersionMethod secondBaseMethod = baseMethod;
		hybridclr::dhe::MetaVersionMethod secondCurrentMethod = secondBaseMethod;
		secondCurrentMethod.version.fill(8);
		secondBaseMetaVersion.methods.push_back(secondBaseMethod);
		secondCurrentMetaVersion.methods.push_back(secondCurrentMethod);
		hybridclr::dhe::MetaVersionData invalidSecondBase = secondBaseMetaVersion;
		hybridclr::dhe::MetaVersionData invalidSecondCurrent = secondCurrentMetaVersion;
		invalidSecondBase.methods[0].token = 0x06000099;
		invalidSecondCurrent.methods[0].token = 0x06000099;
		CHECK(!hybridclr::dhe::PrepareAndRegisterMetaVersions({
			{ &assembly, &baseMetaVersion, &currentMetaVersion },
			{ &secondAssembly, &invalidSecondBase, &invalidSecondCurrent }
		}));
		CHECK(!hybridclr::dhe::IsDheAssembly(&assembly));
		CHECK(!hybridclr::dhe::IsDheAssembly(&secondAssembly));
		CHECK(!changed.isInterpterImpl);
		CHECK(!secondChanged.isInterpterImpl);

		CHECK(hybridclr::dhe::PrepareAndRegisterMetaVersions({
			{ &assembly, &baseMetaVersion, &currentMetaVersion },
			{ &secondAssembly, &secondBaseMetaVersion, &secondCurrentMetaVersion }
		}));
		CHECK(hybridclr::dhe::IsDheAssembly(&assembly));
		CHECK(hybridclr::dhe::IsDheAssembly(&secondAssembly));
		CHECK(hybridclr::dhe::IsChangedMethod(&changed));
		CHECK(hybridclr::dhe::IsChangedMethod(&secondChanged));
		hybridclr::dhe::ResetForTests();
		changed.isInterpterImpl = false;
		secondChanged.isInterpterImpl = false;
		std::free(secondKlass);

		// Tombstones publish removed Base types and methods without requiring
		// an interpreter body. Old native method entries remain resolvable only so
		// their universal guards can raise MissingMethodException.
		hybridclr::dhe::ResetForTests();
		klass->token = 0x02000002;
		baseMetaVersion = hybridclr::dhe::MetaVersionData{};
		currentMetaVersion = hybridclr::dhe::MetaVersionData{};
		baseMetaVersion.assemblyName = assembly.aname.name;
		currentMetaVersion.assemblyName = assembly.aname.name;
		hybridclr::dhe::MetaVersionType removedType;
		removedType.stableId.fill(1);
		removedType.version.fill(2);
		removedType.token = klass->token;
		baseMetaVersion.types.push_back(removedType);
		hybridclr::dhe::MetaVersionMethod removedMethod;
		removedMethod.stableId.fill(3);
		removedMethod.version.fill(4);
		removedMethod.declaringTypeStableId = removedType.stableId;
		removedMethod.token = changed.token;
		removedMethod.flags = 8;
		baseMetaVersion.methods.push_back(removedMethod);
		CHECK(hybridclr::dhe::PrepareAndRegisterMetaVersion(&assembly,
			baseMetaVersion, currentMetaVersion));
		CHECK(hybridclr::dhe::IsRemovedType(klass));
		CHECK(hybridclr::dhe::IsChangedMethod(&changed));
		CHECK(hybridclr::dhe::IsRemovedMethod(&changed));
		MethodInfo currentTokenCollision = changed;
		currentTokenCollision.isInterpterImpl = true;
		CHECK(!hybridclr::dhe::IsChangedMethod(&currentTokenCollision));
		CHECK(!hybridclr::dhe::IsRemovedMethod(&currentTokenCollision));
		CHECK(hybridclr::dhe::ResolveInterpreterMethod(&currentTokenCollision) ==
			&currentTokenCollision);
		hybridclr::native_test::ClearDheResolver();
        hybridclr::dhe::ResetForTests();
        CHECK(!hybridclr::dhe::IsDheAssembly(&assembly));
        std::free(klass);
    }
#endif

    void TestOpcodeDecode()
    {
        using namespace hybridclr::metadata;
        const hybridclr::byte code[] = {
            static_cast<hybridclr::byte>(OpcodeValue::LDC_I4), 1, 0, 0, 0,
            static_cast<hybridclr::byte>(OpcodeValue::PREFIX1), static_cast<hybridclr::byte>(OpcodeValue::CEQ),
            static_cast<hybridclr::byte>(OpcodeValue::BR_S), 0x02,
            static_cast<hybridclr::byte>(OpcodeValue::RET),
        };
        const hybridclr::byte* ip = code;
        const hybridclr::byte* end = code + sizeof(code);
        const OpCodeInfo* first = DecodeOpCodeInfo(ip, end);
        CHECK(first != nullptr);
        CHECK(first->id == OpcodeEnum::LDC_I4);
        CHECK(GetOpCodeSize(ip, first) == 5);
        ip += GetOpCodeSize(ip, first);
        const OpCodeInfo* prefixed = DecodeOpCodeInfo(ip, end);
        CHECK(prefixed != nullptr);
        CHECK(prefixed->id == OpcodeEnum::CEQ);
        CHECK(GetOpCodeSize(ip, prefixed) == 1);
        ip += GetOpCodeSize(ip, prefixed);
        const OpCodeInfo* branch = DecodeOpCodeInfo(ip, end);
        CHECK(branch != nullptr);
        CHECK(branch->inlineType == ArgType::BranchTarget);
        CHECK(GetOpCodeSize(ip, branch) == 2);
        ip += GetOpCodeSize(ip, branch);
        const OpCodeInfo* ret = DecodeOpCodeInfo(ip, end);
        CHECK(ret != nullptr);
        CHECK(ret->id == OpcodeEnum::RET);
        CHECK(GetOpCodeSize(ip, ret) == 1);
        ip += GetOpCodeSize(ip, ret);
        CHECK(ip == end);

        const hybridclr::byte shortData[] = {
            static_cast<hybridclr::byte>(OpcodeValue::LDARG_S), 0x01,
        };
        const hybridclr::byte* shortDataIp = shortData;
        const OpCodeInfo* shortDataInfo = DecodeOpCodeInfo(shortDataIp, shortData + sizeof(shortData));
        CHECK(shortDataInfo != nullptr && shortDataInfo->inlineType == ArgType::Data);
        CHECK(GetOpCodeSize(shortDataIp, shortDataInfo) == 2);

        const hybridclr::byte prefixedData[] = {
            static_cast<hybridclr::byte>(OpcodeValue::PREFIX1),
            static_cast<hybridclr::byte>(OpcodeValue::LDARG), 0x01, 0x00,
        };
        const hybridclr::byte* prefixedDataIp = prefixedData;
        const OpCodeInfo* prefixedDataInfo = DecodeOpCodeInfo(prefixedDataIp, prefixedData + sizeof(prefixedData));
        CHECK(prefixedDataInfo != nullptr && prefixedDataInfo->id == OpcodeEnum::LDARG);
        CHECK(GetOpCodeSize(prefixedDataIp, prefixedDataInfo) == 3);

        const hybridclr::byte longBranch[] = {
            static_cast<hybridclr::byte>(OpcodeValue::BR), 0x01, 0x00, 0x00, 0x00,
            static_cast<hybridclr::byte>(OpcodeValue::NOP),
        };
        const hybridclr::byte* longBranchIp = longBranch;
        const OpCodeInfo* longBranchInfo = DecodeOpCodeInfo(longBranchIp, longBranch + sizeof(longBranch));
        CHECK(longBranchInfo != nullptr && longBranchInfo->inlineType == ArgType::BranchTarget);
        CHECK(GetOpCodeSize(longBranchIp, longBranchInfo) == 5);

        const hybridclr::byte prefixedOnly[] = { static_cast<hybridclr::byte>(OpcodeValue::PREFIX1) };
        const hybridclr::byte* prefixedOnlyIp = prefixedOnly;
        CHECK(DecodeOpCodeInfo(prefixedOnlyIp, prefixedOnly + sizeof(prefixedOnly)) == nullptr);

#if HYBRIDCLR_LAB_HAS_OPTIMIZATION_FACTS
		const hybridclr::byte stableReceiver[] = {
			static_cast<hybridclr::byte>(OpcodeValue::LDARG_0),
			static_cast<hybridclr::byte>(OpcodeValue::LDARGA_S), 0x01,
			static_cast<hybridclr::byte>(OpcodeValue::STARG_S), 0x01,
			static_cast<hybridclr::byte>(OpcodeValue::RET),
		};
		CHECK(hybridclr::transform::HasStableInlineReceiver(stableReceiver, sizeof(stableReceiver)));

		const hybridclr::byte shortReceiverAddress[] = {
			static_cast<hybridclr::byte>(OpcodeValue::LDARGA_S), 0x00,
			static_cast<hybridclr::byte>(OpcodeValue::RET),
		};
		CHECK(!hybridclr::transform::HasStableInlineReceiver(shortReceiverAddress, sizeof(shortReceiverAddress)));

		const hybridclr::byte shortReceiverStore[] = {
			static_cast<hybridclr::byte>(OpcodeValue::STARG_S), 0x00,
			static_cast<hybridclr::byte>(OpcodeValue::RET),
		};
		CHECK(!hybridclr::transform::HasStableInlineReceiver(shortReceiverStore, sizeof(shortReceiverStore)));

		const hybridclr::byte longReceiverAddress[] = {
			static_cast<hybridclr::byte>(OpcodeValue::PREFIX1),
			static_cast<hybridclr::byte>(OpcodeValue::LDARGA), 0x00, 0x00,
			static_cast<hybridclr::byte>(OpcodeValue::RET),
		};
		CHECK(!hybridclr::transform::HasStableInlineReceiver(longReceiverAddress, sizeof(longReceiverAddress)));

		const hybridclr::byte longReceiverStore[] = {
			static_cast<hybridclr::byte>(OpcodeValue::PREFIX1),
			static_cast<hybridclr::byte>(OpcodeValue::STARG), 0x00, 0x00,
			static_cast<hybridclr::byte>(OpcodeValue::RET),
		};
		CHECK(!hybridclr::transform::HasStableInlineReceiver(longReceiverStore, sizeof(longReceiverStore)));

		Il2CppClass* normalClass = static_cast<Il2CppClass*>(std::calloc(1, sizeof(Il2CppClass)));
		CHECK(normalClass != nullptr);
		if (normalClass)
		{
			CHECK(hybridclr::transform::CanDevirtualizeInterfaceReceiver(normalClass));
			normalClass->is_import_or_windows_runtime = 1;
			CHECK(!hybridclr::transform::CanDevirtualizeInterfaceReceiver(normalClass));
			std::free(normalClass);
		}
#endif

        const hybridclr::byte switchCode[] = {
            static_cast<hybridclr::byte>(OpcodeValue::SWITCH),
            0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00,
        };
        const hybridclr::byte* switchIp = switchCode;
        const OpCodeInfo* switchInfo = DecodeOpCodeInfo(switchIp, switchCode + sizeof(switchCode));
        CHECK(switchInfo != nullptr);
        CHECK(switchInfo->inlineType == ArgType::Switch);
        CHECK(GetOpCodeSize(switchIp, switchInfo) == sizeof(switchCode));

        for (int i = 0; i < static_cast<int>(OpcodeEnum::__Count); i++)
        {
            CHECK(g_opcodeInfos[i].id == static_cast<OpcodeEnum>(i));
            CHECK(g_opcodeInfos[i].name[0] != '\0');
        }
    }

    void TestTemporaryMemoryArena()
    {
        using hybridclr::transform::TemporaryMemoryArena;
        CHECK(TemporaryMemoryArena::AligndSize(0) == 0);
        CHECK(TemporaryMemoryArena::AligndSize(1) == 8);
        CHECK(TemporaryMemoryArena::AligndSize(8) == 8);
        CHECK(TemporaryMemoryArena::AligndSize(9) == 16);

        TemporaryMemoryArena arena;
        struct Pair { int left; int right; };
        Pair* pair = arena.AllocIR<Pair>();
        CHECK(pair->left == 0 && pair->right == 0);
        pair->left = 7;
        Pair* second = arena.NewAny<Pair>();
        CHECK(second->left == 0 && second->right == 0);
        int* values = arena.NewNAny<int>(4097);
        values[0] = 11;
        values[4096] = 22;
        CHECK(values[0] == 11 && values[4096] == 22);
        CHECK(reinterpret_cast<uintptr_t>(pair) % 8 == 0);
        CHECK(reinterpret_cast<uintptr_t>(second) % 8 == 0);
        CHECK(arena.NewNAny<int>(0) == nullptr);
        CHECK(arena.NewNAny<int>(-1) == nullptr);

        int* preserved = arena.NewNAny<int>(2000);
        preserved[0] = 17;
        preserved[1999] = 31;
        int* rollover = arena.NewNAny<int>(2000);
        CHECK(rollover != nullptr);
        CHECK(preserved[0] == 17 && preserved[1999] == 31);

        std::vector<int*> manyAllocations;
        manyAllocations.reserve(10000);
        for (int i = 0; i < 10000; i++)
        {
            int* value = arena.NewAny<int>();
            *value = i;
            manyAllocations.push_back(value);
        }
        CHECK(*manyAllocations.front() == 0);
        CHECK(*manyAllocations.back() == 9999);
    }

    void TestCopyHelpers()
    {
        using namespace hybridclr::interpreter;
        std::array<uint8_t, 64> source{};
        std::array<uint8_t, 64> destination{};
        for (size_t i = 0; i < source.size(); i++) source[i] = static_cast<uint8_t>(i + 1);
        Copy1(destination.data(), source.data());
        CHECK(destination[0] == 1);
        Copy4(destination.data() + 4, source.data() + 4);
        CHECK(std::memcmp(destination.data() + 4, source.data() + 4, 4) == 0);
        Copy8(destination.data() + 8, source.data() + 8);
        CHECK(std::memcmp(destination.data() + 8, source.data() + 8, 8) == 0);
        CopyBySize(destination.data() + 16, source.data() + 16, 32);
        CHECK(std::memcmp(destination.data() + 16, source.data() + 16, 32) == 0);
        const std::array<size_t, 7> copySizes = { 12, 16, 20, 24, 28, 32, 37 };
        for (size_t size : copySizes)
        {
            alignas(8) std::array<uint8_t, 96> expected{};
            alignas(8) std::array<uint8_t, 96> actual{};
            for (size_t i = 0; i < expected.size(); i++) expected[i] = actual[i] = static_cast<uint8_t>(i + 3);
            std::memmove(expected.data() + 16, expected.data() + 8, size);
            switch (size)
            {
            case 12: Copy12(actual.data() + 16, actual.data() + 8); break;
            case 16: Copy16(actual.data() + 16, actual.data() + 8); break;
            case 20: Copy20(actual.data() + 16, actual.data() + 8); break;
            case 24: Copy24(actual.data() + 16, actual.data() + 8); break;
            case 28: Copy28(actual.data() + 16, actual.data() + 8); break;
            case 32: Copy32(actual.data() + 16, actual.data() + 8); break;
            default: CopyBySize(actual.data() + 16, actual.data() + 8, static_cast<uint32_t>(size)); break;
            }
            CHECK(actual == expected);
        }
        const std::array<size_t, 6> smallCopySizes = { 0, 1, 2, 4, 8, 9 };
        for (size_t size : smallCopySizes)
        {
            std::array<uint8_t, 32> expected{};
            std::array<uint8_t, 32> actual{};
            for (size_t i = 0; i < expected.size(); i++) expected[i] = actual[i] = static_cast<uint8_t>(i + 17);
            std::memmove(expected.data() + 12, expected.data() + 2, size);
            CopyBySize(actual.data() + 12, actual.data() + 2, static_cast<uint32_t>(size));
            CHECK(actual == expected);
        }
        {
            std::array<uint8_t, 32> expected{};
            std::array<uint8_t, 32> actual{};
            for (size_t i = 0; i < expected.size(); i++) expected[i] = actual[i] = static_cast<uint8_t>(i + 29);
            std::memmove(expected.data() + 2, expected.data() + 12, 16);
            CopyBySize(actual.data() + 2, actual.data() + 12, 16);
            CHECK(actual == expected);
        }
        InitDefaultN(destination.data(), destination.size());
        CHECK(std::all_of(destination.begin(), destination.end(), [](uint8_t value) { return value == 0; }));

        std::array<StackObject, 9> stackSource{};
        std::array<StackObject, 9> stackDestination{};
        stackSource[0].i32 = 11;
        stackSource[1].i32 = 22;
        stackSource[2].i32 = 33;
        stackSource[8].i32 = 99;
        CopyStackObject(stackDestination.data(), stackSource.data(), 3);
        CHECK(stackDestination[0].i32 == 11 && stackDestination[2].i32 == 33);
        CopyStackObject(stackDestination.data(), stackSource.data(), 0);
        CopyStackObject(stackDestination.data(), stackSource.data(), 8);
        CHECK(stackDestination[0].i32 == 11 && stackDestination[2].i32 == 33);
        CopyStackObject(stackDestination.data(), stackSource.data(), 9);
        CHECK(stackDestination[8].i32 == 99);

        InitDefaultN(destination.data(), destination.size());
        CHECK(std::all_of(destination.begin(), destination.end(), [](uint8_t value) { return value == 0; }));
    }

    void TestBasicBlockSplitting()
    {
        using namespace hybridclr;
        using namespace hybridclr::metadata;
        using namespace hybridclr::transform;

        std::array<byte, 22> code{};
        code[0] = static_cast<byte>(OpcodeValue::NOP);
        code[1] = static_cast<byte>(OpcodeValue::BR_S);
        code[2] = 0x02;
        code[3] = static_cast<byte>(OpcodeValue::NOP);
        code[4] = static_cast<byte>(OpcodeValue::SWITCH);
        code[5] = 0x02;
        code[9] = 0x00;
        code[10] = 0x00;
        code[11] = 0x00;
        code[12] = 0x00;
        code[13] = 0x04;
        code[14] = 0x00;
        code[15] = 0x00;
        code[16] = 0x00;
        code[17] = static_cast<byte>(OpcodeValue::NOP);
        code[18] = static_cast<byte>(OpcodeValue::NOP);
        code[19] = static_cast<byte>(OpcodeValue::NOP);
        code[20] = static_cast<byte>(OpcodeValue::NOP);
        code[21] = static_cast<byte>(OpcodeValue::RET);
        MethodBody body{};
        body.codeSize = static_cast<uint32_t>(code.size());
        body.ilcodes = code.data();
        BasicBlockSpliter splitter(body);
        splitter.SplitBasicBlocks();
        const std::set<uint32_t>& offsets = splitter.GetSplitOffsets();
        CHECK(offsets.count(3) == 1);
        CHECK(offsets.count(5) == 1);
        CHECK(offsets.count(17) == 1);
        CHECK(offsets.count(21) == 1);
        CHECK(offsets.count(static_cast<uint32_t>(code.size())) == 1);

        body.exceptionClauses.push_back({ CorILExceptionClauseType::Filter, 0, 1, 16, 2, 8 });
        BasicBlockSpliter withException(body);
        withException.SplitBasicBlocks();
        const std::set<uint32_t>& exceptionOffsets = withException.GetSplitOffsets();
        CHECK(exceptionOffsets.count(0) == 1);
        CHECK(exceptionOffsets.count(1) == 1);
        CHECK(exceptionOffsets.count(16) == 1);
        CHECK(exceptionOffsets.count(18) == 1);
        CHECK(exceptionOffsets.count(8) == 1);

        std::array<byte, 6> longBranchCode{};
        longBranchCode[0] = static_cast<byte>(OpcodeValue::BR);
        longBranchCode[1] = 0x01;
        longBranchCode[5] = static_cast<byte>(OpcodeValue::RET);
        MethodBody longBranchBody{};
        longBranchBody.codeSize = static_cast<uint32_t>(longBranchCode.size());
        longBranchBody.ilcodes = longBranchCode.data();
        BasicBlockSpliter longBranchSplitter(longBranchBody);
        longBranchSplitter.SplitBasicBlocks();
        const std::set<uint32_t>& longBranchOffsets = longBranchSplitter.GetSplitOffsets();
        CHECK(longBranchOffsets.count(5) == 1);
        CHECK(longBranchOffsets.count(6) == 1);

        std::array<byte, 3> zeroBranchCode = {
            static_cast<byte>(OpcodeValue::BR_S), 0, static_cast<byte>(OpcodeValue::RET),
        };
        MethodBody zeroBranchBody{};
        zeroBranchBody.codeSize = static_cast<uint32_t>(zeroBranchCode.size());
        zeroBranchBody.ilcodes = zeroBranchCode.data();
        BasicBlockSpliter zeroBranchSplitter(zeroBranchBody);
        zeroBranchSplitter.SplitBasicBlocks();
        CHECK(zeroBranchSplitter.GetSplitOffsets().count(2) == 0);

        std::array<byte, 3> zeroLeaveCode = {
            static_cast<byte>(OpcodeValue::LEAVE_S), 0, static_cast<byte>(OpcodeValue::RET),
        };
        MethodBody zeroLeaveBody{};
        zeroLeaveBody.codeSize = static_cast<uint32_t>(zeroLeaveCode.size());
        zeroLeaveBody.ilcodes = zeroLeaveCode.data();
        BasicBlockSpliter zeroLeaveSplitter(zeroLeaveBody);
        zeroLeaveSplitter.SplitBasicBlocks();
        CHECK(zeroLeaveSplitter.GetSplitOffsets().count(2) == 1);
        CHECK(zeroLeaveSplitter.GetSplitOffsets().count(3) == 1);

        std::array<byte, 6> finallyCode = {
            static_cast<byte>(OpcodeValue::NOP), static_cast<byte>(OpcodeValue::NOP),
            static_cast<byte>(OpcodeValue::NOP), static_cast<byte>(OpcodeValue::NOP),
            static_cast<byte>(OpcodeValue::NOP), static_cast<byte>(OpcodeValue::RET),
        };
        MethodBody finallyBody{};
        finallyBody.codeSize = static_cast<uint32_t>(finallyCode.size());
        finallyBody.ilcodes = finallyCode.data();
        finallyBody.exceptionClauses.push_back({ CorILExceptionClauseType::Finally, 0, 1, 2, 2, 0 });
        BasicBlockSpliter withFinally(finallyBody);
        withFinally.SplitBasicBlocks();
        CHECK(withFinally.GetSplitOffsets().count(0) == 1);
        CHECK(withFinally.GetSplitOffsets().count(1) == 1);
        CHECK(withFinally.GetSplitOffsets().count(2) == 1);
        CHECK(withFinally.GetSplitOffsets().count(4) == 1);
    }

#if HYBRIDCLR_LAB_HAS_INSTRUCTION_COMBINER
    void TestInstructionCombiner()
    {
        using namespace hybridclr::interpreter;
        using namespace hybridclr::transform;

        CHECK(sizeof(IRLdlocVarVar) == 8);
        CHECK(sizeof(IRLdlocVarVar_2) == 16);
        CHECK(static_cast<uint16_t>(HiOpcodeEnum::LdlocVarVar_2) > static_cast<uint16_t>(HiOpcodeEnum::MethodBaseGetCurrentMethod));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::LdlocVarVar_2)] == 16);
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::InitInlineLocals_n_2)] == sizeof(IRInitInlineLocals_n_2));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::InitInlineLocals_n_4)] == sizeof(IRInitInlineLocals_n_4));
#if defined(HYBRIDCLR_LAB_HAS_ADVANCED_OPTIMIZATION_OPCODES)
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::GetArrayElementVarVar_i4_NoNull)] == sizeof(IRGetArrayElementVarVar_i4_NoNull));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::SetArrayElementVarVar_i4_NoNull)] == sizeof(IRSetArrayElementVarVar_i4_NoNull));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::GetArrayElementVarVar_i4_NoNullNoBounds)] == sizeof(IRGetArrayElementVarVar_i4_NoNullNoBounds));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::SetArrayElementVarVar_i4_NoNullNoBounds)] == sizeof(IRSetArrayElementVarVar_i4_NoNullNoBounds));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::LdfldVarVar_i4_NoNull)] == sizeof(IRLdfldVarVar_i4));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::NewValueTypeCtor_2_i4)] == sizeof(IRNewValueTypeCtor_2_i4));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::NewValueTypeCtor_4_scalar)] == sizeof(IRNewValueTypeCtor_4_scalar));
#endif

        TemporaryMemoryArena arena;
        IRLdlocVarVar* first = arena.AllocIR<IRLdlocVarVar>();
        first->type = HiOpcodeEnum::LdlocVarVar;
        first->dst = 8;
        first->src = 16;
        IRLdlocVarVar* second = arena.AllocIR<IRLdlocVarVar>();
        second->type = HiOpcodeEnum::LdlocVarVar;
        second->dst = 24;
        second->src = 32;

        IRCommon* result = TryCombineLdlocVarVarPair(arena, first, second);
        CHECK(result != nullptr);
        CHECK(result->type == HiOpcodeEnum::LdlocVarVar_2);
        IRLdlocVarVar_2* combined = static_cast<IRLdlocVarVar_2*>(result);
        CHECK(combined->dst0 == 8);
        CHECK(combined->src0 == 16);
        CHECK(combined->dst1 == 24);
        CHECK(combined->src1 == 32);

        // The second copy must observe the first copy's write, just like two
        // original instructions executed in sequence.
        std::array<uint64_t, 4> locals = { 0, 11, 22, 0 };
        IRLdlocVarVar_2 alias = {};
        alias.dst0 = 16;
        alias.src0 = 8;
        alias.dst1 = 24;
        alias.src1 = 16;
        locals[alias.dst0 / sizeof(uint64_t)] = locals[alias.src0 / sizeof(uint64_t)];
        locals[alias.dst1 / sizeof(uint64_t)] = locals[alias.src1 / sizeof(uint64_t)];
        CHECK(locals[2] == 11 && locals[3] == 11);

        IRCommon other{};
        other.type = HiOpcodeEnum::LdcVarConst_4;
        CHECK(TryCombineLdlocVarVarPair(arena, first, &other) == nullptr);
        CHECK(TryCombineLdlocVarVarPair(arena, &other, second) == nullptr);
        CHECK(TryCombineLdlocVarVarPair(arena, result, second) == nullptr);
        CHECK(TryCombineLdlocVarVarPair(arena, nullptr, second) == nullptr);
        CHECK(TryCombineLdlocVarVarPair(arena, first, nullptr) == nullptr);

    }

    void TestLdcAddInstructionCombiner()
    {
        using namespace hybridclr::interpreter;
        using namespace hybridclr::transform;

        CHECK(sizeof(IRLdcVarConst_4_Add_i4) == 16);
        CHECK(sizeof(IRConvertVarVar_i4_i8_Add_i8) == 16);
        CHECK(sizeof(IRLdlocVarVar_3) == 24);
        CHECK(sizeof(IRLdlocVarVar_4) == 32);
        CHECK(sizeof(IRLdlocVarVar_2_LdcVarConst_4) == 24);
        CHECK(sizeof(IRLdcVarConst_4_Add_i4_LdlocVarVar) == 24);
        CHECK(sizeof(IRConvertVarVar_i4_i8_Add_i8_LdlocVarVar_2) == 32);
        CHECK(sizeof(IRLdcVarConst_4_Add_i4_LdlocVarVar_2) == 32);
        CHECK(sizeof(IRLdcVarConst_4_Add_i4_Ret_4) == 16);
        CHECK(static_cast<uint16_t>(HiOpcodeEnum::LdcVarConst_4_Add_i4) > static_cast<uint16_t>(HiOpcodeEnum::LdlocVarVar_2));
        CHECK(g_instructionSizes[static_cast<uint16_t>(HiOpcodeEnum::LdcVarConst_4_Add_i4)] == 16);

        TemporaryMemoryArena arena;
        IRLdcVarConst_4* ldc = arena.AllocIR<IRLdcVarConst_4>();
        ldc->type = HiOpcodeEnum::LdcVarConst_4;
        ldc->dst = 8;
        ldc->src = static_cast<uint32_t>(-7);
        IRBinOpVarVarVar_Add_i4* add = arena.AllocIR<IRBinOpVarVarVar_Add_i4>();
        add->type = HiOpcodeEnum::BinOpVarVarVar_Add_i4;
        add->ret = 16;
        add->op1 = 24;
        add->op2 = 8;
        IRCommon* addResult = TryCombineLdc4AddI4(arena, ldc, add);
        CHECK(addResult != nullptr);
        CHECK(addResult->type == HiOpcodeEnum::LdcVarConst_4_Add_i4);
        IRLdcVarConst_4_Add_i4* combinedAdd = static_cast<IRLdcVarConst_4_Add_i4*>(addResult);
        CHECK(combinedAdd->ret == 16);
        CHECK(combinedAdd->op == 24);
        CHECK(combinedAdd->constant == static_cast<uint32_t>(-7));

        std::array<uint32_t, 8> intLocals{};
        intLocals[combinedAdd->op / sizeof(int32_t)] = 13;
        intLocals[combinedAdd->ret / sizeof(int32_t)] =
            intLocals[combinedAdd->op / sizeof(int32_t)] + combinedAdd->constant;
        CHECK(intLocals[combinedAdd->ret / sizeof(int32_t)] == 6);
        intLocals[combinedAdd->op / sizeof(int32_t)] = std::numeric_limits<uint32_t>::max();
        combinedAdd->constant = 1;
        intLocals[combinedAdd->ret / sizeof(int32_t)] =
            intLocals[combinedAdd->op / sizeof(int32_t)] + combinedAdd->constant;
        CHECK(intLocals[combinedAdd->ret / sizeof(int32_t)] == 0);
        combinedAdd->constant = static_cast<uint32_t>(-7);
        IRCommon other{};
        other.type = HiOpcodeEnum::LdlocVarVar;
        CHECK(TryCombineLdc4AddI4(arena, ldc, &other) == nullptr);
        add->op1 = 30;
        add->op2 = 32;
        CHECK(TryCombineLdc4AddI4(arena, ldc, add) == nullptr);
        add->op1 = 8;
        add->op2 = 8;
        CHECK(TryCombineLdc4AddI4(arena, ldc, add) == nullptr);

        combinedAdd->ret = 16;
        CHECK(TryRedirectStlocDestination(combinedAdd, 16, 56));
        CHECK(combinedAdd->ret == 56);
        CHECK(!TryRedirectStlocDestination(combinedAdd, 16, 64));

        IRRetVar_ret_4 returnI4 = {};
        returnI4.type = HiOpcodeEnum::RetVar_ret_4;
        returnI4.ret = 56;
        IRCommon* returnResult = TryCombineLdc4AddI4Ret4(arena, combinedAdd, &returnI4);
        CHECK(returnResult != nullptr);
        CHECK(returnResult->type == HiOpcodeEnum::LdcVarConst_4_Add_i4_Ret_4);
        IRLdcVarConst_4_Add_i4_Ret_4* combinedReturn = static_cast<IRLdcVarConst_4_Add_i4_Ret_4*>(returnResult);
        CHECK(combinedReturn->ret == 56 && combinedReturn->op == 24);
        CHECK(combinedReturn->constant == static_cast<int32_t>(-7));
        returnI4.ret = 64;
        CHECK(TryCombineLdc4AddI4Ret4(arena, combinedAdd, &returnI4) == nullptr);
        CHECK(TryCombineLdc4AddI4Ret4(arena, &other, &returnI4) == nullptr);

        IRLdlocVarVar_2 redirectLoads = {};
        redirectLoads.type = HiOpcodeEnum::LdlocVarVar_2;
        redirectLoads.dst0 = 8;
        redirectLoads.src0 = 16;
        redirectLoads.dst1 = 24;
        redirectLoads.src1 = 32;
        CHECK(TryRedirectStlocDestination(&redirectLoads, 24, 72));
        CHECK(redirectLoads.dst0 == 8 && redirectLoads.dst1 == 72);

        IRLdlocVarVar_2 loadPair = {};
        loadPair.type = HiOpcodeEnum::LdlocVarVar_2;
        loadPair.dst0 = 16;
        loadPair.src0 = 8;
        loadPair.dst1 = 24;
        loadPair.src1 = 16;
        IRLdlocVarVar thirdLoad = {};
        thirdLoad.type = HiOpcodeEnum::LdlocVarVar;
        thirdLoad.dst = 32;
        thirdLoad.src = 24;
        IRCommon* tripleResult = TryCombineLdlocPairLoad(arena, &loadPair, &thirdLoad);
        CHECK(tripleResult != nullptr && tripleResult->type == HiOpcodeEnum::LdlocVarVar_3);
        IRLdlocVarVar fourthLoad = {};
        fourthLoad.type = HiOpcodeEnum::LdlocVarVar;
        fourthLoad.dst = 40;
        fourthLoad.src = 32;
        IRCommon* quadrupleResult = TryCombineLdlocTripleLoad(arena, tripleResult, &fourthLoad);
        CHECK(quadrupleResult != nullptr && quadrupleResult->type == HiOpcodeEnum::LdlocVarVar_4);
        IRLdlocVarVar_4* quadruple = static_cast<IRLdlocVarVar_4*>(quadrupleResult);
        CHECK(quadruple->dst0 == 16 && quadruple->src0 == 8);
        CHECK(quadruple->dst1 == 24 && quadruple->src1 == 16);
        CHECK(quadruple->dst2 == 32 && quadruple->src2 == 24);
        CHECK(quadruple->dst3 == 40 && quadruple->src3 == 32);
        std::array<uint64_t, 6> quadrupleLocals = { 0, 41, 0, 0, 0, 0 };
        quadrupleLocals[quadruple->dst0 / sizeof(uint64_t)] = quadrupleLocals[quadruple->src0 / sizeof(uint64_t)];
        quadrupleLocals[quadruple->dst1 / sizeof(uint64_t)] = quadrupleLocals[quadruple->src1 / sizeof(uint64_t)];
        quadrupleLocals[quadruple->dst2 / sizeof(uint64_t)] = quadrupleLocals[quadruple->src2 / sizeof(uint64_t)];
        quadrupleLocals[quadruple->dst3 / sizeof(uint64_t)] = quadrupleLocals[quadruple->src3 / sizeof(uint64_t)];
        CHECK(quadrupleLocals[2] == 41 && quadrupleLocals[3] == 41);
        CHECK(quadrupleLocals[4] == 41 && quadrupleLocals[5] == 41);

        IRConvertVarVar_i4_i8* convert = arena.AllocIR<IRConvertVarVar_i4_i8>();
        convert->type = HiOpcodeEnum::ConvertVarVar_i4_i8;
        convert->dst = 8;
        convert->src = 8;
        IRBinOpVarVarVar_Add_i8* addI8 = arena.AllocIR<IRBinOpVarVarVar_Add_i8>();
        addI8->type = HiOpcodeEnum::BinOpVarVarVar_Add_i8;
        addI8->ret = 16;
        addI8->op1 = 8;
        addI8->op2 = 24;
        IRCommon* addI8Result = TryCombineConvertI4I8AddI8(arena, convert, addI8);
        CHECK(addI8Result != nullptr);
        CHECK(addI8Result->type == HiOpcodeEnum::ConvertVarVar_i4_i8_Add_i8);
        IRConvertVarVar_i4_i8_Add_i8* combinedAddI8 = static_cast<IRConvertVarVar_i4_i8_Add_i8*>(addI8Result);
        CHECK(combinedAddI8->ret == 16);
        CHECK(combinedAddI8->converted == 8);
        CHECK(combinedAddI8->other == 24);
        int32_t narrowValue = -7;
        int64_t wideValue = 13;
        int64_t convertedValue = narrowValue;
        int64_t combinedValue = convertedValue + wideValue;
        CHECK(combinedValue == 6);

        IRLdlocVarVar convertLoad0 = {};
        convertLoad0.type = HiOpcodeEnum::LdlocVarVar;
        convertLoad0.dst = 32;
        convertLoad0.src = 40;
        IRLdlocVarVar convertLoad1 = {};
        convertLoad1.type = HiOpcodeEnum::LdlocVarVar;
        convertLoad1.dst = 48;
        convertLoad1.src = 56;
        IRCommon* convertLoadResult = TryCombineConvertAddLoadPair(
            arena, combinedAddI8, &convertLoad0, &convertLoad1);
        CHECK(convertLoadResult != nullptr);
        CHECK(convertLoadResult->type == HiOpcodeEnum::ConvertVarVar_i4_i8_Add_i8_LdlocVarVar_2);
        IRConvertVarVar_i4_i8_Add_i8_LdlocVarVar_2* convertLoads =
            static_cast<IRConvertVarVar_i4_i8_Add_i8_LdlocVarVar_2*>(convertLoadResult);
        CHECK(convertLoads->ret == 16 && convertLoads->converted == 8 && convertLoads->other == 24);
        CHECK(convertLoads->dst0 == 32 && convertLoads->src0 == 40);
        CHECK(convertLoads->dst1 == 48 && convertLoads->src1 == 56);
        uint64_t convertedBits = static_cast<uint64_t>(static_cast<int64_t>(-1));
        uint64_t wrappedWide = convertedBits + 0x8000000000000000ULL;
        CHECK(wrappedWide == 0x7FFFFFFFFFFFFFFFULL);
        CHECK(TryCombineConvertI4I8AddI8(arena, convert, add) == nullptr);
        addI8->op1 = 8;
        addI8->op2 = 8;
        CHECK(TryCombineConvertI4I8AddI8(arena, convert, addI8) == nullptr);
        addI8->op2 = 24;
        convert->src = 16;
        CHECK(TryCombineConvertI4I8AddI8(arena, convert, addI8) == nullptr);

        IRLdlocVarVar* loadedOperand = arena.AllocIR<IRLdlocVarVar>();
        loadedOperand->type = HiOpcodeEnum::LdlocVarVar;
        loadedOperand->dst = 8;

        IRBranchSwitch branchSwitch = {};
        branchSwitch.type = HiOpcodeEnum::BranchSwitch;
        branchSwitch.value = 8;
        branchSwitch.caseNum = 4;
        branchSwitch.caseOffsets = 12;
        loadedOperand->src = 40;
        CHECK(TryPropagateLdlocToConsumer(loadedOperand, &branchSwitch) == &branchSwitch);
        CHECK(branchSwitch.value == 40 && branchSwitch.caseNum == 4 && branchSwitch.caseOffsets == 12);
		branchSwitch.value = 24;
		CHECK(TryPropagateLdlocToConsumer(loadedOperand, &branchSwitch) == nullptr);

		IRLdlocVarVar_3 arrayLoads = {};
        arrayLoads.type = HiOpcodeEnum::LdlocVarVar_3;
        arrayLoads.dst0 = 8;
        arrayLoads.src0 = 40;
        arrayLoads.dst1 = 16;
        arrayLoads.src1 = 48;
        arrayLoads.dst2 = 24;
        arrayLoads.src2 = 56;
        IRGetArrayElementVarVar_i4 getArray = {};
        getArray.type = HiOpcodeEnum::GetArrayElementVarVar_i4;
        getArray.dst = 16;
        getArray.arr = 16;
        getArray.index = 24;
        CHECK(TryReduceLdlocTripleBeforeGetArrayI4(&arrayLoads, &getArray));
        CHECK(arrayLoads.type == HiOpcodeEnum::LdlocVarVar);
        CHECK(arrayLoads.dst0 == 8 && arrayLoads.src0 == 40);
        CHECK(getArray.dst == 16 && getArray.arr == 48 && getArray.index == 56);

        arrayLoads.type = HiOpcodeEnum::LdlocVarVar_3;
        arrayLoads.dst0 = 8;
        arrayLoads.src0 = 40;
        arrayLoads.dst1 = 16;
        arrayLoads.src1 = 8;
        arrayLoads.dst2 = 24;
        arrayLoads.src2 = 16;
        getArray.arr = 16;
        getArray.index = 24;
        CHECK(TryReduceLdlocTripleBeforeGetArrayI4(&arrayLoads, &getArray));
        CHECK(getArray.arr == 40 && getArray.index == 40);
        arrayLoads.type = HiOpcodeEnum::LdlocVarVar_3;
        arrayLoads.src0 = arrayLoads.dst1;
        getArray.arr = arrayLoads.dst1;
        getArray.index = arrayLoads.dst2;
        CHECK(!TryReduceLdlocTripleBeforeGetArrayI4(&arrayLoads, &getArray));
        arrayLoads.type = HiOpcodeEnum::LdlocVarVar_3;
        getArray.arr = 32;
        CHECK(!TryReduceLdlocTripleBeforeGetArrayI4(&arrayLoads, &getArray));

		IRLdlocVarVar_2_LdcVarConst_4 loadPairConstant = {};
        loadPairConstant.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
        loadPairConstant.dst0 = 8;
        loadPairConstant.src0 = 40;
        loadPairConstant.dst1 = 16;
        loadPairConstant.src1 = 48;
        loadPairConstant.ldcDst = 24;
        loadPairConstant.constant = 1;
        IRBinOpVarVarVar_And_i4 branchAnd = {};
        branchAnd.type = HiOpcodeEnum::BinOpVarVarVar_And_i4;
        branchAnd.ret = 16;
        branchAnd.op1 = 16;
        branchAnd.op2 = 24;
        IRCommon* reducedAndResult = TryReduceLdlocPairLdc4AndI4(arena, &loadPairConstant, &branchAnd);
        CHECK(reducedAndResult != nullptr);
        CHECK(loadPairConstant.type == HiOpcodeEnum::LdlocVarVar);
        CHECK(loadPairConstant.dst0 == 8 && loadPairConstant.src0 == 40);
        IRLdcVarConst_4_And_i4* reducedAnd = static_cast<IRLdcVarConst_4_And_i4*>(reducedAndResult);
        CHECK(reducedAnd->ret == 16 && reducedAnd->op == 48 && reducedAnd->constant == 1);

        loadPairConstant.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
        loadPairConstant.dst0 = 8;
        loadPairConstant.src0 = 40;
        loadPairConstant.dst1 = 16;
        loadPairConstant.src1 = 8;
        loadPairConstant.ldcDst = 24;
        branchAnd.ret = 16;
        branchAnd.op1 = 24;
        branchAnd.op2 = 16;
        reducedAndResult = TryReduceLdlocPairLdc4AndI4(arena, &loadPairConstant, &branchAnd);
        CHECK(reducedAndResult != nullptr);
        reducedAnd = static_cast<IRLdcVarConst_4_And_i4*>(reducedAndResult);
        CHECK(reducedAnd->op == 40);
        loadPairConstant.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
        branchAnd.op1 = 32;
        branchAnd.op2 = 24;
        CHECK(TryReduceLdlocPairLdc4AndI4(arena, &loadPairConstant, &branchAnd) == nullptr);
        loadPairConstant.dst0 = loadPairConstant.dst1;
        branchAnd.op1 = loadPairConstant.dst1;
        branchAnd.op2 = loadPairConstant.ldcDst;
        CHECK(TryReduceLdlocPairLdc4AndI4(arena, &loadPairConstant, &branchAnd) == nullptr);
        loadPairConstant.dst0 = 8;
        loadPairConstant.src0 = loadPairConstant.dst1;
        CHECK(TryReduceLdlocPairLdc4AndI4(arena, &loadPairConstant, &branchAnd) == nullptr);

		IRLdlocVarVar_2_LdcVarConst_4 loadPairConstantAdd = {};
		loadPairConstantAdd.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
		loadPairConstantAdd.dst0 = 8;
		loadPairConstantAdd.src0 = 40;
		loadPairConstantAdd.dst1 = 16;
		loadPairConstantAdd.src1 = 48;
		loadPairConstantAdd.ldcDst = 24;
		loadPairConstantAdd.constant = 17;
		IRBinOpVarVarVar_Add_i4 pairConstantAdd = {};
		pairConstantAdd.type = HiOpcodeEnum::BinOpVarVarVar_Add_i4;
		pairConstantAdd.ret = 16;
		pairConstantAdd.op1 = 16;
		pairConstantAdd.op2 = 24;
		IRCommon* reducedAddResult = TryReduceLdlocPairLdc4AddI4(
			arena, &loadPairConstantAdd, &pairConstantAdd);
		CHECK(reducedAddResult != nullptr);
		CHECK(loadPairConstantAdd.type == HiOpcodeEnum::LdlocVarVar);
		CHECK(loadPairConstantAdd.dst0 == 8 && loadPairConstantAdd.src0 == 40);
		IRLdcVarConst_4_Add_i4* reducedAdd = static_cast<IRLdcVarConst_4_Add_i4*>(reducedAddResult);
		CHECK(reducedAdd->ret == 16 && reducedAdd->op == 48 && reducedAdd->constant == 17);
		loadPairConstantAdd.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
		loadPairConstantAdd.src1 = loadPairConstantAdd.dst0;
		pairConstantAdd.op1 = 24;
		pairConstantAdd.op2 = 16;
		reducedAddResult = TryReduceLdlocPairLdc4AddI4(arena, &loadPairConstantAdd, &pairConstantAdd);
		CHECK(reducedAddResult != nullptr);
		CHECK(static_cast<IRLdcVarConst_4_Add_i4*>(reducedAddResult)->op == 40);
		loadPairConstantAdd.type = HiOpcodeEnum::LdlocVarVar_2_LdcVarConst_4;
		pairConstantAdd.op1 = 32;
		pairConstantAdd.op2 = 24;
		CHECK(TryReduceLdlocPairLdc4AddI4(arena, &loadPairConstantAdd, &pairConstantAdd) == nullptr);
		loadPairConstantAdd.dst0 = loadPairConstantAdd.dst1;
		pairConstantAdd.op1 = loadPairConstantAdd.dst1;
		pairConstantAdd.op2 = loadPairConstantAdd.ldcDst;
		CHECK(TryReduceLdlocPairLdc4AddI4(arena, &loadPairConstantAdd, &pairConstantAdd) == nullptr);
		loadPairConstantAdd.dst0 = 8;
		loadPairConstantAdd.src0 = loadPairConstantAdd.dst1;
		CHECK(TryReduceLdlocPairLdc4AddI4(arena, &loadPairConstantAdd, &pairConstantAdd) == nullptr);
        loadedOperand->src = 40;
        IRLdcVarConst_4* loadedConstant = arena.AllocIR<IRLdcVarConst_4>();
        loadedConstant->type = HiOpcodeEnum::LdcVarConst_4;
        loadedConstant->dst = 24;
        loadedConstant->src = 17;
        add->ret = 8;
        add->op1 = 8;
        add->op2 = 24;
        IRCommon* loadedAddResult = TryCombineLdlocLdc4AddI4(arena, loadedOperand, loadedConstant, add);
        CHECK(loadedAddResult != nullptr);
        IRLdcVarConst_4_Add_i4* loadedAdd = static_cast<IRLdcVarConst_4_Add_i4*>(loadedAddResult);
        CHECK(loadedAdd->ret == 8 && loadedAdd->op == 40 && loadedAdd->constant == 17);
        add->op1 = 24;
        add->op2 = 8;
        CHECK(TryCombineLdlocLdc4AddI4(arena, loadedOperand, loadedConstant, add) != nullptr);
        loadedConstant->dst = loadedOperand->dst;
        add->op1 = loadedOperand->dst;
        add->op2 = loadedOperand->dst;
        CHECK(TryCombineLdlocLdc4AddI4(arena, loadedOperand, loadedConstant, add) == nullptr);
        loadedConstant->dst = 24;
        loadedOperand->src = loadedConstant->dst;
        CHECK(TryCombineLdlocLdc4AddI4(arena, loadedOperand, loadedConstant, add) == nullptr);
        loadedOperand->src = 40;

        IRBinOpVarVarVar_And_i4* andI4 = arena.AllocIR<IRBinOpVarVarVar_And_i4>();
        andI4->type = HiOpcodeEnum::BinOpVarVarVar_And_i4;
        andI4->ret = 8;
        andI4->op1 = 8;
        andI4->op2 = 24;
		IRCommon* loadedAndResult = TryCombineLdlocLdc4AndI4(arena, loadedOperand, loadedConstant, andI4);
		CHECK(loadedAndResult != nullptr);
		IRLdcVarConst_4_And_i4* loadedAnd = static_cast<IRLdcVarConst_4_And_i4*>(loadedAndResult);
		CHECK(loadedAnd->ret == 8 && loadedAnd->op == 40 && loadedAnd->constant == 17);

		IRBinOpVarVarVar_Mul_i4* loadedMultiplyI4 = arena.AllocIR<IRBinOpVarVarVar_Mul_i4>();
		loadedMultiplyI4->type = HiOpcodeEnum::BinOpVarVarVar_Mul_i4;
		loadedMultiplyI4->ret = 8;
		loadedMultiplyI4->op1 = 8;
		loadedMultiplyI4->op2 = 24;
		IRCommon* loadedMultiplyI4Result = TryCombineLdlocLdc4MulI4(
			arena, loadedOperand, loadedConstant, loadedMultiplyI4);
		CHECK(loadedMultiplyI4Result != nullptr);
		IRLdcVarConst_4_Mul_i4* loadedMultiplyI4Combined =
			static_cast<IRLdcVarConst_4_Mul_i4*>(loadedMultiplyI4Result);
		CHECK(loadedMultiplyI4Combined->ret == 8 && loadedMultiplyI4Combined->op == 40 &&
			loadedMultiplyI4Combined->constant == 17);
		loadedMultiplyI4->op1 = 24;
		loadedMultiplyI4->op2 = 8;
		CHECK(TryCombineLdlocLdc4MulI4(arena, loadedOperand, loadedConstant, loadedMultiplyI4) != nullptr);
		loadedOperand->src = loadedConstant->dst;
		CHECK(TryCombineLdlocLdc4MulI4(arena, loadedOperand, loadedConstant, loadedMultiplyI4) == nullptr);
		loadedOperand->src = 40;
        loadedMultiplyI4->op1 = 32;
        loadedMultiplyI4->op2 = 48;
        CHECK(TryCombineLdlocLdc4MulI4(arena, loadedOperand, loadedConstant, loadedMultiplyI4) == nullptr);

        IRBitShiftBinOpVarVarVar_Shr_i4_i4* loadedShift =
            arena.AllocIR<IRBitShiftBinOpVarVarVar_Shr_i4_i4>();
        loadedShift->type = HiOpcodeEnum::BitShiftBinOpVarVarVar_Shr_i4_i4;
        loadedShift->ret = 8;
        loadedShift->value = 8;
        loadedShift->shiftAmount = 24;
        loadedConstant->src = 45;
        IRCommon* loadedShiftResult = TryCombineLdlocLdc4ShrI4(
            arena, loadedOperand, loadedConstant, loadedShift);
        CHECK(loadedShiftResult != nullptr);
        IRLdcVarConst_4_Shr_i4_i4* loadedShiftCombined =
            static_cast<IRLdcVarConst_4_Shr_i4_i4*>(loadedShiftResult);
        CHECK(loadedShiftCombined->ret == 8 && loadedShiftCombined->value == 40 &&
            loadedShiftCombined->shiftAmount == 13);
        loadedShift->value = 32;
        CHECK(TryCombineLdlocLdc4ShrI4(arena, loadedOperand, loadedConstant, loadedShift) == nullptr);
        loadedShift->value = loadedConstant->dst;
        CHECK(TryCombineLdc4ShrI4(arena, loadedConstant, loadedShift) == nullptr);
        loadedShift->value = loadedOperand->dst;

        IRLdcVarConst_8* loadedDoubleConstant = arena.AllocIR<IRLdcVarConst_8>();
        loadedDoubleConstant->type = HiOpcodeEnum::LdcVarConst_8;
        loadedDoubleConstant->dst = 24;
        double doubleConstant = 1.5;
        std::memcpy(&loadedDoubleConstant->src, &doubleConstant, sizeof(doubleConstant));
        IRBinOpVarVarVar_Mul_f8* multiplyF8 = arena.AllocIR<IRBinOpVarVarVar_Mul_f8>();
        multiplyF8->type = HiOpcodeEnum::BinOpVarVarVar_Mul_f8;
        multiplyF8->ret = 8;
        multiplyF8->op1 = 8;
        multiplyF8->op2 = 24;
        IRCommon* loadedMultiplyResult = TryCombineLdlocLdc8MulF8(
            arena, loadedOperand, loadedDoubleConstant, multiplyF8);
        CHECK(loadedMultiplyResult != nullptr);
        IRLdcVarConst_8_Mul_f8* loadedMultiply = static_cast<IRLdcVarConst_8_Mul_f8*>(loadedMultiplyResult);
        CHECK(loadedMultiply->ret == 8 && loadedMultiply->op == 40 && loadedMultiply->constant == 1.5);
        uint64_t nanBits = 0x7FF8000000000042ULL;
        std::memcpy(&loadedDoubleConstant->src, &nanBits, sizeof(nanBits));
        loadedMultiplyResult = TryCombineLdlocLdc8MulF8(
            arena, loadedOperand, loadedDoubleConstant, multiplyF8);
        CHECK(loadedMultiplyResult != nullptr);
        uint64_t combinedNanBits = 0;
        std::memcpy(&combinedNanBits,
            &static_cast<IRLdcVarConst_8_Mul_f8*>(loadedMultiplyResult)->constant,
            sizeof(combinedNanBits));
        CHECK(combinedNanBits == nanBits);
        loadedOperand->src = loadedDoubleConstant->dst;
        CHECK(TryCombineLdlocLdc8MulF8(arena, loadedOperand, loadedDoubleConstant, multiplyF8) == nullptr);
        loadedOperand->src = 40;
        multiplyF8->op1 = 32;
        multiplyF8->op2 = 48;
        CHECK(TryCombineLdlocLdc8MulF8(arena, loadedOperand, loadedDoubleConstant, multiplyF8) == nullptr);

        IRLdlocVarVar* branchLoad = arena.AllocIR<IRLdlocVarVar>();
        branchLoad->type = HiOpcodeEnum::LdlocVarVar;
        branchLoad->dst = 8;
        branchLoad->src = 40;
        IRBranchVarVar_Clt_i4* branch = arena.AllocIR<IRBranchVarVar_Clt_i4>();
        branch->type = HiOpcodeEnum::BranchVarVar_Clt_i4;
        branch->op1 = 8;
        branch->op2 = 24;
        branch->offset = 123;
        IRCommon* branchResult = TryPropagateLdlocToBranchCltI4(arena, branchLoad, branch);
        CHECK(branchResult != nullptr);
        CHECK(branchResult->type == HiOpcodeEnum::BranchVarVar_Clt_i4);
        IRBranchVarVar_Clt_i4* propagatedBranch = static_cast<IRBranchVarVar_Clt_i4*>(branchResult);
        CHECK(propagatedBranch->op1 == 40);
        CHECK(propagatedBranch->op2 == 24);
        CHECK(propagatedBranch->offset == 123);

        branch->op1 = 24;
        branch->op2 = 8;
        branchResult = TryPropagateLdlocToBranchCltI4(arena, branchLoad, branch);
        CHECK(branchResult != nullptr);
        propagatedBranch = static_cast<IRBranchVarVar_Clt_i4*>(branchResult);
        CHECK(propagatedBranch->op1 == 24 && propagatedBranch->op2 == 40);
        branch->op1 = 8;
        branch->op2 = 8;
        branchResult = TryPropagateLdlocToBranchCltI4(arena, branchLoad, branch);
        CHECK(branchResult != nullptr);
        propagatedBranch = static_cast<IRBranchVarVar_Clt_i4*>(branchResult);
        CHECK(propagatedBranch->op1 == 40 && propagatedBranch->op2 == 40);
        IRCommon nonBranch{};
        nonBranch.type = HiOpcodeEnum::BinOpVarVarVar_Add_i4;
        CHECK(TryPropagateLdlocToBranchCltI4(arena, branchLoad, &nonBranch) == nullptr);
        branch->op1 = 24;
        branch->op2 = 32;
        CHECK(TryPropagateLdlocToBranchCltI4(arena, branchLoad, branch) == nullptr);

        IRLdlocVarVar_2* branchLoads = arena.AllocIR<IRLdlocVarVar_2>();
        branchLoads->type = HiOpcodeEnum::LdlocVarVar_2;
        branchLoads->dst0 = 8;
        branchLoads->src0 = 40;
        branchLoads->dst1 = 24;
        branchLoads->src1 = 48;
        branch->op1 = 8;
        branch->op2 = 24;
        branch->offset = 456;
        branchResult = TryPropagateLdlocPairToBranchCltI4(arena, branchLoads, branch);
        CHECK(branchResult != nullptr);
        propagatedBranch = static_cast<IRBranchVarVar_Clt_i4*>(branchResult);
        CHECK(propagatedBranch->op1 == 40 && propagatedBranch->op2 == 48);
        CHECK(propagatedBranch->offset == 456);

        // The second copy observes the first copy before the branch executes.
        branchLoads->src1 = branchLoads->dst0;
        branch->op1 = branchLoads->dst0;
        branch->op2 = branchLoads->dst1;
        branchResult = TryPropagateLdlocPairToBranchCltI4(arena, branchLoads, branch);
        CHECK(branchResult != nullptr);
        propagatedBranch = static_cast<IRBranchVarVar_Clt_i4*>(branchResult);
        CHECK(propagatedBranch->op1 == 40 && propagatedBranch->op2 == 40);
        branch->op2 = 64;
        CHECK(TryPropagateLdlocPairToBranchCltI4(arena, branchLoads, branch) == nullptr);

        branchLoads->dst0 = branchLoads->dst1;
        branch->op1 = branchLoads->dst0;
        branch->op2 = branchLoads->dst1;
        CHECK(TryPropagateLdlocPairToBranchCltI4(arena, branchLoads, branch) == nullptr);

        IRStfldVarVar_i4 storeField = {};
        storeField.type = HiOpcodeEnum::StfldVarVar_i4;
        storeField.obj = 8;
        storeField.offset = 4;
        storeField.data = 24;
        branchLoads->dst0 = 8;
        branchLoads->src0 = 40;
        branchLoads->dst1 = 24;
        branchLoads->src1 = 48;
        CHECK(TryPropagateLdlocPairToStfldI4(branchLoads, &storeField) != nullptr);
        CHECK(storeField.obj == 40 && storeField.data == 48);
        branchLoads->src0 = branchLoads->dst1;
        storeField.obj = branchLoads->dst0;
        storeField.data = branchLoads->dst1;
        CHECK(TryPropagateLdlocPairToStfldI4(branchLoads, &storeField) == nullptr);

    }
#endif
}

int main()
{
#if HYBRIDCLR_LAB_FGS_TESTS
    TestManagedToNativeCallSelection();
#endif
    TestBlobReader();
    TestMetadataUtilities();
#if HYBRIDCLR_LAB_DHE_ENABLED
    TestDheMethodRegistry();
#endif
    TestOpcodeDecode();
    TestTemporaryMemoryArena();
    TestCopyHelpers();
    TestBasicBlockSplitting();
#if HYBRIDCLR_LAB_HAS_INSTRUCTION_COMBINER
    TestInstructionCombiner();
    TestLdcAddInstructionCombiner();
#endif
    if (g_failures != 0)
    {
        std::cerr << "HybridCLR native tests failed: " << g_failures << '\n';
        return 1;
    }

#if HYBRIDCLR_LAB_HAS_INSTRUCTION_COMBINER
    std::cout << "HybridCLR native tests: 9 groups passed\n";
#else
    std::cout << "HybridCLR native tests: 7 groups passed\n";
#endif
    return 0;
}
