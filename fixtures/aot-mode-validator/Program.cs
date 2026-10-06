using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HybridCLR.Editor.BuildProcessors;
using dnlib.DotNet;

class Program
{
    static void Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);
        string[] deferred={"StartupHotfix"};
        var positive=Path.Combine(root,"base/StartupHotfix.dll");
        var consumer=Path.Combine(root,"shared/StartupConsumer.dll");
        AotModeDependencyValidator.Validate(new[]{positive},deferred);
        AotModeDependencyValidator.Validate(new[]{positive,consumer},new[]{"StartupHotfix","StartupConsumer"});
        bool rejected=false;
        try { AotModeDependencyValidator.Validate(new[]{positive,consumer},deferred); }
        catch(InvalidOperationException error) { rejected=error.Message.Contains("StartupConsumer")&&error.Message.Contains("StartupHotfix"); }
        if(!rejected)throw new Exception("Static AOT reference was accepted.");
        // Genuine IL compiler output from the previous counterexample, not a mock.
        var previous=Path.Combine(root,"projects/DHE/Library/Bee/PlayerScriptAssemblies/Assembly-CSharp.dll");
        bool oldRejected=false;
        try { AotModeDependencyValidator.Validate(new[]{positive,previous},deferred); }
        catch(InvalidOperationException error) { oldRejected=error.Message.Contains("Assembly-CSharp"); }
        if(!oldRejected)throw new Exception("The observed static-reference counterexample was accepted.");
        foreach(string unsafeAssembly in args.Skip(1))
        {
            if(!unsafeAssembly.Contains("CALLBACK")) {
                AotModeDependencyValidator.Validate(new[]{unsafeAssembly},deferred);
                continue;
            }
            bool unsafeRejected=false;
            try { AotModeDependencyValidator.Validate(new[]{unsafeAssembly},deferred); }
            catch(InvalidOperationException error) { unsafeRejected=error.Message.Contains("deferred"); }
            if(!unsafeRejected)throw new Exception("Unsafe Unity registration was accepted: "+unsafeAssembly);
        }
        Console.WriteLine(JsonSerializer.Serialize(new {passes=4+args.Length-1,staticReferenceRejected=rejected,observedCounterexampleRejected=oldRejected,hotfixToHotfixAllowed=true,dynamicUnityTypesAllowed=true,automaticStartupCallbackRejected=true}));
    }
}
