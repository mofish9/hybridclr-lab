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
        var support=Assembly.Load(File.ReadAllBytes(Path.Combine(args[0],"shared/StartupAotSupport.dll")));
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>new AssemblyName(eventArgs.Name).Name=="StartupAotSupport" ? support : null;
        var assembly=Assembly.Load(current);
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,eventArgs)=>new AssemblyName(eventArgs.Name).Name=="StartupHotfix" ? assembly : null;
        var actual=Workload.Run(assembly,Assembly.Load(consumer));
        File.WriteAllText(args[1],JsonSerializer.Serialize(new{actual,caseCount=actual.Length,currentSha256=Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant(),consumerSha256=Convert.ToHexString(SHA256.HashData(consumer)).ToLowerInvariant()}));
    }
}
