// Windows test Player only. No native startup or platform code enters the library.
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <vector>
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

extern "C" __declspec(dllexport) uint32_t CheckTypeEnumeration(uint32_t phase)
{
    HMODULE module=GetModuleHandleW(L"GameAssembly.dll");
    auto count=reinterpret_cast<size_t(*)(void*)>(GetProcAddress(module,"il2cpp_image_get_class_count"));
    auto get=reinterpret_cast<void*(*)(void*,size_t)>(GetProcAddress(module,"il2cpp_image_get_class"));
    auto name=reinterpret_cast<const char*(*)(void*)>(GetProcAddress(module,"il2cpp_class_get_name"));
    auto image=reinterpret_cast<void*(*)(void*)>(GetProcAddress(module,"il2cpp_class_get_image"));
    if(!count||!get||!name||!image||!hotfixImage||!unityImage)return 0;
    uint32_t found=0;bool consistent=true,stable=true,removed=false;
    void* images[]={hotfixImage,unityImage};
    for(void* current:images){
        size_t n=count(current);std::vector<void*> seen;
        for(size_t i=0;i<n;++i){
            void* c=get(current,i);
            if(!c){consistent=false;continue;}
            for(void* previous:seen)if(previous==c)consistent=false;
            seen.push_back(c);
            if(image(c)!=current)consistent=false;
            const char* typeName=name(c);
            if(!std::strcmp(typeName,"AddedType"))found|=1;
            if(!std::strcmp(typeName,"AddedWorker"))found|=2;
            if(!std::strcmp(typeName,"AddedData"))found|=4;
            if(!std::strcmp(typeName,"Nested"))found|=8;
            if(!std::strcmp(typeName,"AddedGeneric`1"))found|=16;
            if(!std::strcmp(typeName,"RemovedType")||!std::strcmp(typeName,"RemovedNested"))removed=true;
        }
        // Repeated enumeration must preserve count, order and class identity.
        if(n!=count(current))stable=false;
        for(size_t i=0;i<seen.size();++i)if(seen[i]!=get(current,i))stable=false;
    }
    if(!phase)return removed?1:0; // Warm the Base view after selection, before Current.
    return found|(!removed?32:0)|(consistent?64:0)|(stable?128:0);
}
