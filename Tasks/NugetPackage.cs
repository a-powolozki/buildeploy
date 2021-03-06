﻿// -----------------------------------------------------------------------
// <copyright file="NugetPackage.cs" company="">
// TODO: Update copyright text.
// </copyright>
// -----------------------------------------------------------------------

namespace Cms.Buildeploy.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.IO;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;
    using System.Globalization;
    using System.Diagnostics;
    using System.Threading;
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class NugetPackage : PackageTaskBase
    {

        [Required]
        public string NugetExePath { get; set; }

        [Required]
        public string NuspecFile { get; set; }

        [Required]
        public string OutputDirectory { get; set; }

        public string PackageId { get; set; }

        public string ApiKey { get; set; }

        public string PushLocation { get; set; }

        public bool NoDefaultExcludes { get; set; }

        protected override IPackageArchive CreatePackageArchive()
        {
            var archive = new NugetArchive(NugetExePath, Path.GetFullPath(NuspecFile), OutputDirectory, Version, Log);
            if (!string.IsNullOrWhiteSpace(PackageId))
            { 
                archive.AddProperty("Id", PackageId);
                archive.AddProperty("PackageId", PackageId);
            }

            archive.ApiKey = ApiKey;
            archive.PushLocation = PushLocation;
            archive.NoDefaultExcludes = this.NoDefaultExcludes;

            return archive;
        }

        protected override string ReplaceDirectorySeparators(string entryName)
        {
            return entryName;
        }
    }

    class NugetArchive : IPackageArchive
    {

        private readonly string tempPath;
        private readonly string nugetPath;
        private readonly string nuspecFile;
        private readonly string version;
        private readonly string outputDir;
        private readonly Dictionary<string, string> properties = new Dictionary<string, string>();
        internal NugetArchive(string nugetPath, string nuspecFile, string outputDir, string version, TaskLoggingHelper log)
        {
            this.nugetPath = nugetPath;
            this.nuspecFile = nuspecFile;
            this.version = version;
            Log = log;
            tempPath = CreateTempDirectory();
            this.outputDir = Path.GetFullPath(outputDir);
        }


        private static string CreateTempDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        internal string PushLocation { get; set; }

        internal string ApiKey { get; set; }

        internal bool NoDefaultExcludes { get; set; }

        internal void AddProperty(string name, string value)
        {
            properties.Add(name, value);
        }


        private string BuildPropertiesString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var entry in properties)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}={1};", entry.Key, entry.Value);
            }

            return sb.ToString();
        }
        private TaskLoggingHelper Log { get; set; }


        public void AddEntry(string entryName, DateTime dateTime, System.IO.FileStream stream)
        {
            string fileName = Path.Combine(tempPath, entryName);
            string directory = Path.GetDirectoryName(fileName);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (var destStream = File.Create(fileName))
            {
                stream.CopyTo(destStream);
            }
            File.SetLastWriteTime(fileName, dateTime);
        }

        public bool Finish()
        {
            StringBuilder commandLine = new StringBuilder();


            string nupkgTempDirectory = CreateTempDirectory();
            try
            {
                commandLine.AppendFormat(CultureInfo.InvariantCulture,
                    "pack \"{0}\"  -NoPackageAnalysis -BasePath \"{1}\" -OutputDirectory \"{2}\" -Version {3}", nuspecFile, tempPath, nupkgTempDirectory, version);
                if (this.NoDefaultExcludes)
                {
                    commandLine.Append(" -NoDefaultExcludes");
                }

                foreach (var fileName in Directory.GetFiles(nupkgTempDirectory))
                    File.Move(fileName, Path.Combine(outputDir, Path.GetFileName(fileName)));

                string propertiesString = BuildPropertiesString();
                if (!string.IsNullOrWhiteSpace(propertiesString))
                    commandLine.AppendFormat(CultureInfo.InvariantCulture, " -Properties {0}", propertiesString);

                Log.LogMessage(MessageImportance.Low, "NuGet.exe path: {0} ", nugetPath);

                if (!RunNuget(commandLine.ToString())) return false;

                string packageFileName = Directory.GetFiles(nupkgTempDirectory, "*.nupkg").Single();

                string pushCommandLine = CreatePushCommandLine(packageFileName);
                if (!string.IsNullOrWhiteSpace(pushCommandLine))
                {
                    Log.LogMessage(MessageImportance.Normal, "Pushing package {0} to {1}", Path.GetFileName(packageFileName), PushLocation);
                    if (!RunNuget(pushCommandLine)) return false;
                    Log.LogMessage(MessageImportance.Normal, "Push complete.");
                }

                string destFileName = Path.Combine(outputDir, Path.GetFileName(packageFileName));
                if (File.Exists(destFileName))
                {
                    Log.LogError("Cannot copy package to '{0}', file already exists.", destFileName);
                    return false;
                }
                File.Copy(packageFileName, destFileName);
                Log.LogMessage(MessageImportance.Normal, "Created package '{0}'", destFileName);
                return true;
            }
            finally
            {
                TryRemoveTempDirectory(nupkgTempDirectory);
            }
        }

        private string CreatePushCommandLine(string packageFileName)
        {

            if (!string.IsNullOrWhiteSpace(PushLocation))
            {
                StringBuilder sb = new StringBuilder("push ");
                sb.Append(packageFileName);
                if (!string.IsNullOrWhiteSpace(ApiKey))
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " -ApiKey {0}", ApiKey);
                }
                sb.AppendFormat(CultureInfo.InvariantCulture, " -Source \"{0}\"", PushLocation);

                return sb.ToString();
            }
            else
                return null;
        }
        private bool RunNuget(string commandLine)
        {
            Log.LogMessage(MessageImportance.Low, "Running NuGet.exe with command line arguments: {0}", commandLine);

            var exitCode = SilentProcessRunner.ExecuteCommand(
                nugetPath,
                commandLine,
                tempPath,
                output => Log.LogMessage(MessageImportance.Low, output),
                error => Log.LogError("NUGET: {0}", error));

            if (exitCode != 0)
            {
                Log.LogError("There was an error calling NuGet. Please see the output above for more details. Command line: '{0}' {1}", nugetPath, commandLine);
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            TryRemoveTempDirectory(tempPath);

        }

        private static void TryRemoveTempDirectory(string tempDirectory)
        {
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                }
                catch (IOException) { }
            }
        }
    }

    public static class SilentProcessRunner
    {
        public static int ExecuteCommand(string executable, string arguments, string workingDirectory, Action<string> output, Action<string> error)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;

                    using (var outputWaitHandle = new AutoResetEvent(false))
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (e.Data == null)
                            {
                                outputWaitHandle.Set();
                            }
                            else
                            {
                                output(e.Data);
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (e.Data == null)
                            {
                                errorWaitHandle.Set();
                            }
                            else
                            {
                                error(e.Data);
                            }
                        };

                        process.Start();

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();
                        outputWaitHandle.WaitOne();
                        errorWaitHandle.WaitOne();

                        return process.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error when attempting to execute {0}: {1}", executable, ex.Message), ex);
            }
        }
    }
}
