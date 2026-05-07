using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SPNet.Workflow.WfSerializer
{
    public sealed class CacheAssemblyResolver : IDisposable
    {
        private readonly string _cacheFolder;

        public CacheAssemblyResolver(string cacheFolder)
        {
            _cacheFolder = cacheFolder;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromCacheFolder;
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveFromCacheFolder;
        }

        private Assembly? ResolveFromCacheFolder(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name).Name;
            if (string.IsNullOrWhiteSpace(requested)) return null;

            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, requested, StringComparison.OrdinalIgnoreCase));
            if (alreadyLoaded != null) return alreadyLoaded;

            var candidate = Path.Combine(_cacheFolder, requested + ".dll");
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);

            if (string.Equals(requested, "Microsoft.Activities", StringComparison.OrdinalIgnoreCase))
            {
                var microsoftActivitiesProxy = Path.Combine(_cacheFolder, "Microsoft.Activities.Proxy.dll");
                if (File.Exists(microsoftActivitiesProxy)) return Assembly.LoadFrom(microsoftActivitiesProxy);
            }

            var proxyCandidate = Path.Combine(_cacheFolder, requested + ".Proxy.dll");
            return File.Exists(proxyCandidate) ? Assembly.LoadFrom(proxyCandidate) : null;
        }
    }
}
