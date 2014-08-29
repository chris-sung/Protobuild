// ====================================================================== //
// This source code is licensed in accordance with the licensing outlined //
// on the main Tychaia website (www.tychaia.com).  Changes to the         //
// license on the website apply retroactively.                            //
// ====================================================================== //
using System;
using Protobuild.Tasks;
using System.Net;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.IO.Compression;

namespace Protobuild.Submodules
{
    public class SubmoduleManager
    {
        public void ResolveAll(ModuleInfo module, string platform, bool source)
        {
            if (module.Submodules == null)
            {
                return;
            }

            Console.WriteLine("Starting resolution of submodules...");

            foreach (var submodule in module.Submodules)
            {
                Console.WriteLine("Resolving: " + submodule.Uri);
                this.Resolve(submodule, platform, source);
            }

            Console.WriteLine("Submodule resolution complete.");
        }

        public void Resolve(SubmoduleRef reference, string platform, bool source)
        {
            var baseUri = reference.UriObject;

            var indexUri = new Uri(baseUri + "/index");
            var indexData = this.GetStringList(indexUri);

            if (indexData.Length == 0)
            {
                throw new InvalidOperationException(
                    "The specified submodule reference is not valid.");
            }

            var sourceUri = indexData[0];

            Directory.CreateDirectory(reference.Folder);

            if (source)
            {
                this.ResolveSource(reference, sourceUri);
            }
            else
            {
                this.ResolveBinary(reference, platform, sourceUri, indexData);
            }
        }

        private void ResolveSource(SubmoduleRef reference, string source)
        {
            if (File.Exists(Path.Combine(reference.Folder, ".git")))
            {
                return;
            }

            this.EmptyReferenceFolder(reference.Folder);

            this.UnmarkIgnored(reference.Folder);
            this.RunGit(null, "submodule update --init --recursive");

            if (!File.Exists(Path.Combine(reference.Folder, ".git")))
            {
                // The submodule has never been added.
                this.RunGit(null, "submodule add " + source + " " + reference.Folder);
                this.RunGit(reference.Folder, "checkout -f " + reference.GitRef);
                this.RunGit(null, "submodule update --init --recursive");
                this.RunGit(null, "add .gitmodules");
                this.RunGit(null, "add " + reference.Folder);
            }

            this.MarkIgnored(reference.Folder);
        }

        private void ResolveBinary(SubmoduleRef reference, string platform, string source, string[] indexData)
        {
            if (File.Exists(Path.Combine(reference.Folder, platform, ".pkg")))
            {
                return;
            }

            var folder = Path.Combine(reference.Folder, platform);

            Directory.CreateDirectory(folder);

            this.EmptyReferenceFolder(folder);

            this.MarkIgnored(reference.Folder);

            var availableRefs = indexData.ToList();
            availableRefs.RemoveAt(0);

            if (!availableRefs.Contains(reference.GitRef))
            {
                this.ResolveSource(reference, source);
                return;
            }

            var baseUri = reference.UriObject;

            var platformsUri = new Uri(baseUri + "/" + reference.GitRef + "/platforms");
            var platforms = this.GetStringList(platformsUri);

            var platformName = platform;

            if (!platforms.Contains(platformName))
            {
                this.ResolveSource(reference, source);
                return;
            }

            var packageData = this.GetBinary(baseUri + "/" + reference.GitRef + "/" + platformName + ".tar.gz");

            using (var memory = new MemoryStream(packageData))
            {
                using (var decompress = new GZipStream(memory, CompressionMode.Decompress))
                {
                    using (var memory2 = new MemoryStream())
                    {
                        decompress.CopyTo(memory2);
                        memory2.Seek(0, SeekOrigin.Begin);

                        var reader = new tar_cs.TarReader(memory2);

                        reader.ReadToEnd(folder);
                    }
                }
            }

            var file = File.Create(Path.Combine(folder, ".pkg"));
            file.Close();
        }

        private byte[] GetBinary(string packageUri)
        {
            try 
            {
                using (var client = new WebClient())
                {
                    return client.DownloadData(packageUri);
                }
            }
            catch (WebException)
            {
                Console.WriteLine("Web exception when retrieving: " + packageUri);
                throw;
            }
        }

        private void UnmarkIgnored(string folder)
        {
            var excludePath = this.GetGitExcludePath(folder);

            if (excludePath == null)
            {
                return;
            }

            var contents = this.GetFileStringList(excludePath).ToList();
            contents.Remove(folder);
            this.SetFileStringList(excludePath, contents);
        }

        private void MarkIgnored(string folder)
        {
            var excludePath = this.GetGitExcludePath(folder);

            if (excludePath == null)
            {
                return;
            }

            var contents = this.GetFileStringList(excludePath).ToList();
            contents.Add(folder);
            this.SetFileStringList(excludePath, contents);
        }

        private string GetGitExcludePath(string folder)
        {
            var root = this.GetGitRootPath(folder);

            if (root == null)
            {
                return null;
            }
            else 
            {
                return Path.Combine(root, ".git", "info", "exclude");
            }
        }

        private string GetGitRootPath(string folder)
        {
            var current = folder;

            while (current != null && !Directory.Exists(Path.Combine(folder, ".git")))
            {
                var parent = new DirectoryInfo(current).Parent;

                if (parent == null)
                {
                    current = null;
                }
                else 
                {
                    current = parent.FullName;
                }
            }

            return current;
        }

        private void RunGit(string folder, string str)
        {
            throw new NotImplementedException();
        }

        private void SetFileStringList(string excludePath, IEnumerable<string> contents)
        {
            using (var writer = new StreamWriter(excludePath, false))
            {
                foreach (var line in contents)
                {
                    writer.WriteLine(line);
                }
            }
        }

        private IEnumerable<string> GetFileStringList(string excludePath)
        {
            var results = new List<string>();

            using (var reader = new StreamReader(excludePath))
            {
                while (!reader.EndOfStream)
                {
                    results.Add(reader.ReadLine());
                }
            }

            return results;
        }

        private void EmptyReferenceFolder(string folder)
        {
            Directory.Delete(folder, true);
        }

        private string[] GetStringList(Uri indexUri)
        {
            try
            {
                using (var client = new WebClient())
                {
                    return client.DownloadString(indexUri).Split(
                        new char[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch (WebException)
            {
                Console.WriteLine("Web exception when retrieving: " + indexUri);
                throw;
            }
        }
    }
}

