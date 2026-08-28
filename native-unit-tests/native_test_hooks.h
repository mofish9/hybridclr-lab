#pragma once

#include <cstddef>
#include <cstdint>

#include "il2cpp-class-internals.h"

namespace hybridclr
{
namespace native_test
{
    void SetAOTMetadataAvailable(bool available);
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
}
}
