using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Breeze.ThirdPartyLicenseOverview
{
    public class Generator
    {
        string _solutionFile;

        public Generator(string solutionFile)
        {
            _solutionFile = solutionFile;
        }

        public async Task Run()
        {
            if (!File.Exists(_solutionFile))
                throw new ApplicationException("Solution file not found");
            Out.Info($"# 3rd Party Licenses for {_solutionFile}");
            Regex getProjects = new Regex("Project\\([^\\)]*\\)\\s*=\\s*\\\"(?'name'[^\\\"]+)\\\"\\s*,\\s*\\\"(?'path'.+\\.csproj)\\\"");
            var solutionFileText = File.ReadAllText(_solutionFile);
            Match match = getProjects.Match(solutionFileText);

            List<string> projects = new List<string>();
            List<Library> libraries = new List<Library>();

            var basePath = Path.GetDirectoryName(_solutionFile);
            while (match.Success)
            {
                Out.Debug($"Solution Project {match.Groups[1].Value}, {match.Groups[2].Value}");
                projects.Add(Path.Combine(basePath, match.Groups[2].Value));
                match = match.NextMatch();
            }
            int index = 0;
            while (index < projects.Count)
            {
                var format = AnalyzeProjectFile(projects, index);
                switch (format)
                {
                    case ProjectFormat.PackagesConfig:
                        FindPackageConfigLibraries(libraries, projects[index]);
                        break;
                    case ProjectFormat.InlinePackageReferences:
                        FindPackageReferences(libraries, projects[index]);
                        break;
                    default:
                        Out.Error($"Unknown project format {projects[index]}");
                        break;
                }
                index++;
            }

            using (HttpClient httpClient = new HttpClient())
            {
                foreach (var lib in libraries)
                {
                    var response = await httpClient.GetAsync($"https://api-v2v3search-0.nuget.org/query?q={lib.Name}&SemVer={lib.Version}");
                    if (response.IsSuccessStatusCode)
                    {
                        JObject o = JObject.Parse(await response.Content.ReadAsStringAsync());
                        var data = o.Property("data").Value;
                        bool found = false;
                        foreach (JObject item in data)
                            if (item.Property("id").Value.ToString() == lib.Name)
                            {
                                lib.Summary = item.Property("summary")?.Value?.ToString();
                                var authors = (JArray)item.Property("authors").Value;
                                lib.Authors = String.Join(", ", authors.Values<string>());
                                lib.LicenseUrl = item.Property("licenseUrl")?.Value?.ToString();
                                found = true;
                                break;
                            }
                        if (!found)
                            lib.Name += "- THIS PACKAGE IS NOT FOUND!";
                    }
                    else
                        throw new ApplicationException($"Can't obtain information for {lib.Name} package.");

                    if (!String.IsNullOrEmpty(lib.LicenseUrl))
                        lib.LicenseName = await RecognizeLicense(httpClient, lib.LicenseUrl);

                    Out.Info($"## {lib.Name} ({lib.Version})");
                    Out.Info($"- License: [{lib.LicenseName}]({lib.LicenseUrl})");
                    Out.Info($"- Authors: {lib.Authors}");
                    if (!String.IsNullOrWhiteSpace(lib.Summary))
                        Out.Info($"- Summary: {lib.Summary}");
                    Out.Info("");
                }
            }
        }

        private async Task<string> RecognizeLicense(HttpClient httpClient, string licenseUrl)
        {
            var response = await httpClient.GetAsync(licenseUrl);
            if (response.IsSuccessStatusCode)
            {
                string text = await response.Content.ReadAsStringAsync();
                if (text.Contains("MIT License"))
                    return "MIT";
                else if (text.Contains("MIT"))
                    return "MIT";
                else if (text.Contains("The BSD License"))
                    return "BSD License";
                else if (text.Contains("Apache License 2.0"))
                    return "Apache License 2.0";
                else if (text.Contains("Apache License") && text.Contains("Version 2.0"))
                    return "Apache License 2.0";
                else if (text.Contains("Apache-2.0"))
                    return "Apache License 2.0";
                else if (text.Contains("MICROSOFT SOFTWARE LICENSE"))
                    return "MICROSOFT SOFTWARE LICENSE";
                else if (text.Contains("BSD-3-Clause") || text.Contains("BSD 3-Clause"))
                    return "BSD-3-Clause";
                else if (text.Contains("MS-PL"))
                    return "MS-PL";
                else if (text.Contains("GNU LESSER GENERAL PUBLIC LICENSE"))
                    return "LGPL";
                else
                    return "UNKNOWN";
            }
            else
                return response.ReasonPhrase;
        }

        private void FindPackageConfigLibraries(List<Library> libraries, string projectFileName)
        {
            var basePath = Path.GetDirectoryName(projectFileName);
            var packageConfigName = Path.Combine(basePath, "packages.config");
            if (!File.Exists(packageConfigName))
                throw new ApplicationException($"Packages.config not found for project {projectFileName}");
            using (var reader = new StreamReader(packageConfigName))
            {
                var document = new XmlDocument();
                document.Load(reader);
                foreach (XmlElement node in document.SelectNodes("/packages/package"))
                {
                    if (!libraries.Any(x => x.Name == node.Attributes["id"].Value))
                        libraries.Add(new Library
                        {
                            Name = node.Attributes["id"].Value,
                            Version = node.Attributes["version"].Value
                        });
                }
            }
        }

        private void FindPackageReferences(List<Library> libraries, string projectFile)
        {
            using (var reader = new StreamReader(projectFile))
            {
                var document = new XmlDocument();
                document.Load(reader);
                foreach (XmlElement node in document.SelectNodes("/Project/ItemGroup/PackageReference"))
                {
                    var name = node.GetAttribute("Include");
                    if (!libraries.Any(p => p.Name == name))
                    {
                        libraries.Add(new Library
                        {
                            Name = name,
                            Version = node.GetAttribute("Version")
                        });
                    }
                }
            }
        }

        private ProjectFormat AnalyzeProjectFile(List<string> projects, int index)
        {
            string vs2003Namespace = "http://schemas.microsoft.com/developer/msbuild/2003";
            var projectName = projects[index];
            var basePath = Path.GetDirectoryName(projectName);

            using (var reader = new StreamReader(projectName))
            {
                var document = new XmlDocument();
                document.Load(reader);

                if (document.DocumentElement.HasAttribute("xmlns") && document.DocumentElement.Attributes["xmlns"].Value == vs2003Namespace)
                {
                    var xnManager = new XmlNamespaceManager(document.NameTable);
                    xnManager.AddNamespace("x", vs2003Namespace);

                    foreach (XmlElement node in document.SelectNodes("/x:Project/x:ItemGroup/x:ProjectReference", xnManager))
                    {
                        var name = Path.GetFullPath(Path.Combine(basePath, node.GetAttribute("Include")));
                        if (!projects.Contains(name))
                        {
                            projects.Add(name);
                            Out.Debug($"Add project reference {name}");
                        }
                    }
                    return ProjectFormat.PackagesConfig;
                }
                else
                    return ProjectFormat.InlinePackageReferences;
            }
        }
    }

    public enum ProjectFormat
    {
        Unknown,
        PackagesConfig,
        InlinePackageReferences
    }
}
