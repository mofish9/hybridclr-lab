cmake_minimum_required(VERSION 3.20)
project(HybridCLRNativeTests LANGUAGES CXX)

set(HYBRIDCLR_RUNTIME_ROOT "${CMAKE_CURRENT_LIST_DIR}/../staging/runtime/Baseline-Clean/libil2cpp" CACHE PATH "Merged libil2cpp source root")
set(HYBRIDCLR_TEST_SOURCE_ROOT "${HYBRIDCLR_RUNTIME_ROOT}/hybridclr" CACHE PATH "HybridCLR source root inside libil2cpp")
set(HYBRIDCLR_TEST_UNITY_VERSION 20220362 CACHE STRING "Numeric Unity compatibility version")
set(HYBRIDCLR_TEST_TUANJIE_VERSION 11000 CACHE STRING "Numeric Tuanjie version, or zero for Unity")
set(HYBRIDCLR_TEST_FULL_GENERIC_SHARING 0 CACHE BOOL "Build tests for the full generic sharing bridge")
set(IL2CPP_BASELIB_INCLUDE "C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t12/Editor/Data/il2cpp/external/baselib/Include" CACHE PATH "Engine baselib headers")
set(IL2CPP_BASELIB_PLATFORM_INCLUDE "C:/Program Files/Tuanjie/Hub/Editor/2022.3.62t12/Editor/Data/il2cpp/external/baselib/Platforms/Windows/Include" CACHE PATH "Engine Windows baselib headers")
set(IL2CPP_EXTERNAL "${CMAKE_CURRENT_LIST_DIR}/../staging/runtime/Baseline-Clean/external" CACHE PATH "Engine il2cpp external headers")

if (NOT EXISTS "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/Opcodes.cpp")
    message(FATAL_ERROR "HybridCLR runtime sources were not assembled at ${HYBRIDCLR_TEST_SOURCE_ROOT}. Run the C# host assemble-runtime command first.")
endif()

file(READ "${HYBRIDCLR_RUNTIME_ROOT}/il2cpp-class-internals.h" HYBRIDCLR_CLASS_INTERNALS)
if (HYBRIDCLR_CLASS_INTERNALS MATCHES "fullGenericSharingPreparationState")
	set(HYBRIDCLR_TEST_HAS_PREPARATION_STATE 1)
else()
	set(HYBRIDCLR_TEST_HAS_PREPARATION_STATE 0)
endif()

file(READ "${HYBRIDCLR_TEST_SOURCE_ROOT}/interpreter/Instruction.h" HYBRIDCLR_INSTRUCTION_HEADER)
if (HYBRIDCLR_INSTRUCTION_HEADER MATCHES "NewValueTypeCtor_4_scalar")
    set(HYBRIDCLR_TEST_HAS_ADVANCED_OPTIMIZATION_OPCODES 1)
else()
    set(HYBRIDCLR_TEST_HAS_ADVANCED_OPTIMIZATION_OPCODES 0)
endif()

if (HYBRIDCLR_TEST_EXPECT_DHE)
    if (NOT EXISTS "${HYBRIDCLR_TEST_SOURCE_ROOT}/DheRuntime.cpp" OR
        NOT EXISTS "${HYBRIDCLR_TEST_SOURCE_ROOT}/DheRuntime.h")
        message(FATAL_ERROR "DHE test profile requires hybridclr/DheRuntime.cpp and hybridclr/DheRuntime.h")
    endif()
endif()

set(HYBRIDCLR_DHE_SOURCES)
if (EXISTS "${HYBRIDCLR_TEST_SOURCE_ROOT}/DheRuntime.cpp")
    list(APPEND HYBRIDCLR_DHE_SOURCES "${HYBRIDCLR_TEST_SOURCE_ROOT}/DheRuntime.cpp")
endif()

add_executable(hybridclr_native_tests
    hybridclr_native_tests.cpp
    native_runtime_stubs.cpp
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/Il2CppCompatibleDef.cpp"
    ${HYBRIDCLR_DHE_SOURCES}
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/interpreter/Instruction.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/Opcodes.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/transform/BasicBlockSpliter.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/transform/TemporaryMemoryArena.cpp"
)

add_library(hybridclr_metadata_compile_gate OBJECT
    "${HYBRIDCLR_RUNTIME_ROOT}/metadata/ArrayMetadata.cpp"
    "${HYBRIDCLR_RUNTIME_ROOT}/metadata/GenericMethod.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/Assembly.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/AOTHomologousImage.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/ClassFieldLayoutCalculator.cpp"
	"${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/Image.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/InterpreterImage.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/MetadataModule.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/SuperSetAOTHomologousImage.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/metadata/VTableSetup.cpp"
)
add_dependencies(hybridclr_native_tests hybridclr_metadata_compile_gate)

add_library(hybridclr_runtime_api_compile_gate OBJECT
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/RuntimeApi.cpp"
)

add_library(hybridclr_interpreter_compile_gate OBJECT
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/interpreter/Interpreter_Execute.cpp"
    "${HYBRIDCLR_TEST_SOURCE_ROOT}/transform/TransformContext.cpp"
)

add_library(hybridclr_engine_reflection_compile_gate OBJECT
    "${HYBRIDCLR_RUNTIME_ROOT}/vm/Image.cpp"
    "${HYBRIDCLR_RUNTIME_ROOT}/vm/Class.cpp"
    "${HYBRIDCLR_RUNTIME_ROOT}/vm/Field.cpp"
    "${HYBRIDCLR_RUNTIME_ROOT}/icalls/mscorlib/System.Reflection/FieldInfo.cpp"
    "${HYBRIDCLR_RUNTIME_ROOT}/icalls/mscorlib/System.Reflection/RuntimeFieldInfo.cpp"
)

function(configure_hybridclr_target target)
    target_compile_features(${target} PRIVATE cxx_std_17)
    target_compile_definitions(${target} PRIVATE
        HYBRIDCLR_LAB_NATIVE_TEST=1
        HYBRIDCLR_UNITY_VERSION=${HYBRIDCLR_TEST_UNITY_VERSION}
        HYBRIDCLR_UNITY_2019_OR_NEW=1
        HYBRIDCLR_UNITY_2020_OR_NEW=1
        BASELIB_INLINE_NAMESPACE=il2cpp_baselib
    )

    if (HYBRIDCLR_TEST_UNITY_VERSION GREATER_EQUAL 20210000)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_UNITY_2021_OR_NEW=1)
    endif()
    if (HYBRIDCLR_TEST_UNITY_VERSION GREATER_EQUAL 20220000)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_UNITY_2022=1 HYBRIDCLR_UNITY_2022_OR_NEW=1)
    elseif (HYBRIDCLR_TEST_UNITY_VERSION GREATER_EQUAL 20210000)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_UNITY_2021=1)
    endif()
    if (HYBRIDCLR_TEST_TUANJIE_VERSION GREATER 0)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_TUANJIE_VERSION=${HYBRIDCLR_TEST_TUANJIE_VERSION})
    endif()
    if (HYBRIDCLR_TEST_FULL_GENERIC_SHARING)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_LAB_FGS_TESTS=1)
    endif()
    if (HYBRIDCLR_TEST_HAS_PREPARATION_STATE)
		target_compile_definitions(${target} PRIVATE HYBRIDCLR_LAB_HAS_PREPARATION_STATE=1)
    endif()
    if (HYBRIDCLR_TEST_HAS_ADVANCED_OPTIMIZATION_OPCODES)
        target_compile_definitions(${target} PRIVATE HYBRIDCLR_LAB_HAS_ADVANCED_OPTIMIZATION_OPCODES=1)
    endif()
    target_include_directories(${target} PRIVATE
        "${HYBRIDCLR_RUNTIME_ROOT}"
        "${HYBRIDCLR_TEST_SOURCE_ROOT}"
        "${IL2CPP_BASELIB_INCLUDE}"
        "${IL2CPP_BASELIB_PLATFORM_INCLUDE}"
        "${IL2CPP_EXTERNAL}"
    )

    if (MSVC)
        target_compile_options(${target} PRIVATE /W4 /EHsc /permissive- /utf-8)
        target_compile_options(${target} PRIVATE /FI"${CMAKE_CURRENT_LIST_DIR}/native_test_compat.h")
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
    endif()
endfunction()

configure_hybridclr_target(hybridclr_native_tests)
configure_hybridclr_target(hybridclr_metadata_compile_gate)
configure_hybridclr_target(hybridclr_runtime_api_compile_gate)
configure_hybridclr_target(hybridclr_interpreter_compile_gate)
configure_hybridclr_target(hybridclr_engine_reflection_compile_gate)

enable_testing()
add_test(NAME hybridclr_native_tests COMMAND hybridclr_native_tests)
set_tests_properties(hybridclr_native_tests PROPERTIES TIMEOUT 30)
