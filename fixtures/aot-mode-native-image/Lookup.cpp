// Windows test Player only. No native startup or platform code enters the library.
#include <windows.h>
#include <cstdint>
static void* hotfixImage;
static void* unityImage;
extern "C" __declspec(dllexport) uint32_t CheckCurrentTypes(uint32_t phase)
{
    HMODULE module=GetModuleHandleW(L"GameAssembly.dll");
    if(!module)return 0;
    auto domain=reinterpret_cast<void*(*)()>(GetProcAddress(module,"il2cpp_domain_get"));
    auto assembly=reinterpret_cast<void*(*)(void*,const char*)>(GetProcAddress(module,"il2cpp_domain_assembly_open"));
    auto image=reinterpret_cast<void*(*)(void*)>(GetProcAddress(module,"il2cpp_assembly_get_image"));
    auto klass=reinterpret_cast<void*(*)(void*,const char*,const char*)>(GetProcAddress(module,"il2cpp_class_from_name"));
    if(!domain||!assembly||!image||!klass)return 0;
    void* a=assembly(domain(),"StartupHotfix"), *b=assembly(domain(),"StartupUnityHotfix");
    if(!a||!b)return 0;
    void* i=image(a), *u=image(b);
    if(!i||!u)return 0;
    if(!phase){hotfixImage=i;unityImage=u;return 0;}
    uint32_t result=(i==hotfixImage&&u==unityImage)?32:0;
    if(klass(i,"StartupHotfix","Entry"))result|=1;
    if(klass(i,"StartupHotfix","AddedType"))result|=2;
    if(klass(u,"StartupUnityHotfix","Worker"))result|=4;
    if(klass(u,"StartupUnityHotfix","AddedWorker"))result|=8;
    if(klass(u,"StartupUnityHotfix","AddedData"))result|=16;
    return result;
}
