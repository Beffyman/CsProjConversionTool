using Microsoft.Build.Evaluation;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CsProjConversionTool
{
	public static class ProjectMutators
	{

		public static Project DeleteNonIncludedFiles(this Project proj)
		{
			var folderPath = Path.GetFullPath($"{proj.ProjectFileLocation.File}/..");

			List<string> expectedFiles = new List<string>();
			expectedFiles.AddRange(proj.GetItems("Compile").Select(x => x.EvaluatedInclude));
			expectedFiles.AddRange(proj.GetItems("None").Select(x => x.EvaluatedInclude));
			expectedFiles.AddRange(proj.GetItems("Content").Select(x => x.EvaluatedInclude));
			expectedFiles.AddRange(proj.GetItems("EmbeddedResource").Select(x => x.EvaluatedInclude));


			var dependents = proj.Items.Where(x => x.HasMetadata("DependentUpon")).Select(x => $"{x.EvaluatedInclude}/../{x.GetMetadataValue("DependentUpon")}").ToList();
			expectedFiles.AddRange(dependents);

			expectedFiles = expectedFiles.Select(x => Path.GetFullPath($"{folderPath}\\{x}")).Distinct().ToList();
			expectedFiles.Add(proj.ProjectFileLocation.File);



			var actualFiles = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).Distinct().ToList();

			var filesToDelete = actualFiles.Except(expectedFiles, StringComparer.CurrentCultureIgnoreCase).ToList();

			foreach (var deleteMe in filesToDelete)
			{
				File.Delete(deleteMe);
			}

			return proj;
		}



		public static Project TransformReferences(this Project proj)
		{
			var nugetReferences = proj.GetItems("Reference").ToList();


			Dictionary<string, string> packagesToAdd = new Dictionary<string, string>();

			//Remove package references
			foreach (var package in nugetReferences)
			{
				var hintData = package.GetMetadata("HintPath");
				if (hintData == null)
				{
					continue;
				}

				var hintPathSplit = hintData.EvaluatedValue.Split('\\');
				var packageDetails = hintPathSplit[2];
				var splitDetails = packageDetails.Split('.').ToList();
				int nonVersionDetail = 0;
				bool erroring = true;
				bool skip = false;

				int offset = 0;
				string packageName = null;
				string packageVersion = null;

				while (erroring)
				{
					try
					{
						try
						{
							nonVersionDetail = splitDetails.IndexOf(splitDetails.First(x => x.All(c => char.IsNumber(c)))) - offset;//Find the first full number Value between the .'s  Package.Name.*1*.0.0
						}
						catch//This is the case where it isn't a package, but it is a dll reference
						{
							skip = true;
							erroring = false;
							break;
						}

						packageName = string.Join(".", splitDetails.Take(nonVersionDetail));
						packageVersion = string.Join(".", splitDetails.Skip(nonVersionDetail));

						NuGetVersion.Parse(packageVersion);
						erroring = false;
					}
					catch (Exception)
					{
						offset--;
					}
				}


				if (skip)
				{
					continue;
				}



				if (packagesToAdd.ContainsKey(packageName))
				{
					var existing = NuGetVersion.Parse(packagesToAdd[packageName]);
					var attempt = NuGetVersion.Parse(packageVersion);
					if (existing < attempt)
					{
						Console.WriteLine($"{packageName}.{packagesToAdd[packageName]} has been converted changed to {packageVersion} due to detection of a higher version being used.");
						packagesToAdd[packageName] = packageVersion;
					}
				}
				else
				{
					packagesToAdd.Add(packageName, packageVersion);
				}

				proj.RemoveItem(package);
			}

			foreach (var package in packagesToAdd)
			{
				var metaData = new List<KeyValuePair<string, string>>
				{
					new KeyValuePair<string, string>("Version", package.Value),
					//new KeyValuePair<string, string>("PrivateAssets", "All")
				};

				proj.AddItem("PackageReference", package.Key, metaData);
			}

			return proj;
		}

		public static Project DeletePackageConfig(this Project proj)
		{
			var fullPath = Path.GetFullPath($"{proj.ProjectFileLocation.LocationString}/../packages.config");

			var packagesConfigItem = proj.GetItemsByEvaluatedInclude("packages.config").SingleOrDefault();
			if (packagesConfigItem != null)
			{
				proj.RemoveItem(packagesConfigItem);
				File.Delete(fullPath);
			}

			return proj;
		}

		public static Project AddMigrationPackages(this Project proj)
		{
			if (Path.GetFileNameWithoutExtension(proj.ProjectFileLocation.File).Contains("Tests"))
			{
				var testMetaData = new List<KeyValuePair<string, string>>
				{
					new KeyValuePair<string, string>("Version", "1.1.18")
				};

				proj.AddItem("PackageReference", "MSTest.TestAdapter", testMetaData);
				proj.AddItem("PackageReference", "MSTest.TestFramework", testMetaData);
			}


			return proj;
		}

		public static Project TransformProjectType(this Project proj)
		{
			proj.Xml.Sdk = "Microsoft.NET.Sdk";
			//proj.Xml.DefaultTargets = null;
			proj.Xml.ToolsVersion = null;
			var imports = proj.Xml.Imports.ToList();

			foreach (var import in imports)
			{

				//Remove known .Net Framework imports
				if (import.Project.Contains("Microsoft.Common.props")
					|| import.Project.Contains("Microsoft.CSharp.targets")
					|| import.Project.Contains("Microsoft.Web.Publishing.targets")
					|| import.Project.Contains("Microsoft.TestTools.targets"))
				{
					proj.Xml.RemoveChild(import);
				}

				if (import.Project.Contains("Microsoft.WebApplication.targets")
					&& import.Project.Contains("VSToolsPath"))
				{

					var targetsMetaData = new List<KeyValuePair<string, string>>
					{
						new KeyValuePair<string, string>("Version", "14.0.0.3")
					};

					proj.AddItem("PackageReference", "MSBuild.Microsoft.VisualStudio.Web.targets", targetsMetaData);
				}
			}

			//Alter Target Version to use .net core's style

			var targetFramework = proj.GetProperty("TargetFrameworkVersion");
			if (targetFramework == null)
			{
				throw new Exception("It seems like this project is not a .Net Framework project, aborting.");
			}

			var version = targetFramework.EvaluatedValue.Replace("v", "net").Replace(".", "");

			proj.RemoveProperty(targetFramework);

			proj.SetProperty("TargetFramework", version);


			return proj;
		}

		public static Project AddRequiredProperties(this Project proj)
		{

			proj.SetProperty("RestoreProjectStyle", "PackageReference");
			proj.SetProperty("EnableDefaultEmbeddedResourceItems", "false");
			proj.SetProperty("AutoGenerateBindingRedirects", "true");
			proj.SetProperty("GenerateBindingRedirectsOutputType", "true");

			return proj;
		}

		public static Project SaveProject(this Project proj)
		{
			proj.Save();

			return proj;
		}


		public static Project RemoveAssemblyInfo(this Project proj)
		{
			var assemblyInfos = proj.Items.Where(x => x.UnevaluatedInclude.Contains("AssemblyInfo.cs")).ToList();
			proj.RemoveItems(assemblyInfos);

			return proj;
		}


		public static Project RemoveCompileItems(this Project proj)
		{
			var compileItems = proj.GetItems("Compile").ToList();
			var removedCompileItems = compileItems.Where(x => x.Metadata.Count == 0 && (x.DirectMetadata?.Count() ?? 0) == 0).ToList();

			proj.RemoveItems(removedCompileItems);


			var modifiedCompileItems = compileItems.Where(x => !(x.Metadata.Count == 0 && (x.DirectMetadata?.Count() ?? 0) == 0)).ToList();

			foreach (var item in modifiedCompileItems)
			{
				var update = item.UnevaluatedInclude;
				item.Xml.Include = null;//Can't just set because there is a constraint on the set for these
				item.Xml.Update = update;
			}

			return proj;
		}
		public static Project UpdateNoneItems(this Project proj)
		{
			var compileItems = proj.GetItems("None").ToList();

			var modifiedCompileItems = compileItems.Where(x => !(x.Metadata.Count == 0 && (x.DirectMetadata?.Count() ?? 0) == 0)).ToList();

			foreach (var item in modifiedCompileItems)
			{
				var update = item.UnevaluatedInclude;
				item.Xml.Include = null;//Can't just set because there is a constraint on the set for these
				item.Xml.Update = update;
			}

			return proj;
		}

		public static Project RemoveProjectReferenceGuids(this Project proj)
		{

			var projectRefs = proj.GetItems("ProjectReference").ToList();
			foreach (var pref in projectRefs)
			{
				if (pref.HasMetadata("Project"))
				{
					pref.RemoveMetadata("Project");
				}

				if (pref.HasMetadata("Name"))
				{
					pref.RemoveMetadata("Name");
				}
			}


			return proj;
		}


		public static Project MutatePackageReference(this Project proj, string name, string newVersion)
		{
			foreach (var item in proj.GetItems("PackageReference"))
			{
				if (item.UnevaluatedInclude == name)
				{
					item.SetMetadataValue("Version", newVersion);
				}
			}


			return proj;
		}


		public static string GetPackageVersion(this Project proj, string name)
		{
			foreach (var item in proj.GetItems("PackageReference"))
			{
				if (item.UnevaluatedInclude == name)
				{
					return item.GetMetadataValue("Version");
				}
			}


			return null;
		}
	}
}
