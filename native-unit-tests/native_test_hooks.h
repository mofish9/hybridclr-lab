#pragma once

#include <cstddef>
#include <cstdint>
#include <stdexcept>

#include "il2cpp-class-internals.h"

namespace hybridclr
{
namespace native_test
{
    enum class VmExceptionKind { ExecutionEngine, MissingMethod };
    struct RaisedVmException : std::runtime_error
    {
        RaisedVmException(VmExceptionKind value, const std::string& message)
            : std::runtime_error(message), kind(value) {}
        VmExceptionKind kind;
    };
    // Per-thread opt-in; unexpected VM exceptions still abort normal fixtures.
    void CaptureVmExceptions(bool enabled);
    void SetAOTMetadataAvailable(bool available);
    void SetDheSupplementalMethod(const MethodInfo* method);
    void ThrowOnInterpreterMethodPointer(const MethodInfo* method);
    bool VerifyDheAttributePropertyIndices();
    InvokerMethod GetInterpreterInvoker();
    Il2CppMethodPointer GetInterpreterMethodPointer();
    void PauseNextAOTMetadataQuery();
    bool WaitForPausedAOTMetadataQuery(uint32_t timeoutMilliseconds);
    void ResumeAOTMetadataQuery();
    uint64_t GetAOTMetadataQueryAttemptCount();
    bool WaitForAOTMetadataQueryAttemptAfter(uint64_t count, uint32_t timeoutMilliseconds);
    void AcquireAOTMetadataLock();
    void ReleaseAOTMetadataLock();

    // Configure the minimal metadata lookup surface used by the native DHE
    // transaction test. The production runtime supplies these VM services;
    // the standalone test executable uses this explicit fixture instead.
    void ConfigureDheResolver(Il2CppAssembly* assembly, Il2CppImage* image, Il2CppClass* klass);
    void ClearDheResolver();
    void ConfigurePhysicalType(const Il2CppType* type, Il2CppClass* klass);
    void ClearPhysicalTypes();
    void SetDhePhysicalSelection(const Il2CppType* before, const Il2CppType* after);
    uint64_t GetDheResolverEnumerationCount();
}
}
