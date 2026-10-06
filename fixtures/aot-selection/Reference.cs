using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
class Reference
{
    static void Main(string[] args)
    {
        byte[] current=File.ReadAllBytes(Path.Combine(args[0],"shared/StartupHotfix.dll"));
        byte[] consumer=File.ReadAllBytes(Path.Combine(args[0],"shared/StartupConsumer.dll"));
        var assembly=Assembly.Load(current);
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>new AssemblyName(eventArgs.Name).Name=="StartupHotfix" ? assembly : null;
        var entry=assembly.GetType("StartupHotfix.Entry",true);
        var other=Assembly.Load(consumer).GetType("StartupConsumer.Entry",true);
        int Call(string name,params object[] values)=>(int)entry.GetMethod(name).Invoke(null,values);
        var actual=new[]{Call("Changed",5),Call("Unchanged",7),Call("InterfaceCall",6),Call("DelegateCall",8),Call("ValueCall",12),Call("StaticCall",19),Call("ExceptionCall"),Call("Added",3),
            (int)entry.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null,new object[]{23}),
            (int)assembly.GetType("StartupHotfix.AddedType",true).GetField("Value").GetValue(Activator.CreateInstance(assembly.GetType("StartupHotfix.AddedType",true))),
            (int)other.GetMethod("Run").Invoke(null,null)};
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{actual,caseCount=actual.Length,currentSha256=Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant(),consumerSha256=Convert.ToHexString(SHA256.HashData(consumer)).ToLowerInvariant()}));
    }
}
