using System;
using System.Reflection;

static class Workload
{
    public static readonly int[] Expected = {205,21,24,208,21,19,17,34,23,27,229,51,43,37,8,9,13,31,26};
    public static int[] Run(Assembly assembly, Assembly consumer)
    {
        Type entry=assembly.GetType("StartupHotfix.Entry",true);
        int Call(string name, params object[] args) => (int)entry.GetMethod(name).Invoke(null,args);
        return new[] {
            Call("Changed",5),Call("Unchanged",7),Call("InterfaceCall",6),Call("DelegateCall",8),
            Call("ValueCall",12),Call("StaticCall",19),Call("ExceptionCall"),Call("Added",3),
            (int)entry.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null,new object[]{23}),
            (int)assembly.GetType("StartupHotfix.AddedType",true).GetField("Value").GetValue(Activator.CreateInstance(assembly.GetType("StartupHotfix.AddedType",true))),
            (int)consumer.GetType("StartupConsumer.Entry",true).GetMethod("Run").Invoke(null,null),
            Call("FieldReflection"),Call("PropertyReflection"),Call("Attributes"),Call("EventCall"),
            Call("ArrayCopy"),Call("GenericValue"),Call("Cctor"),Call("Finally")
        };
    }
}
