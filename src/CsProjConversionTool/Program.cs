using CsProjArrange;
using System;
using Microsoft.Build.Evaluation;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace CsProjConversionTool
{
	public static class Program
	{
		static void Main(string[] args)
		{
			if (args.Count() != 1)
			{
				throw new Exception("There can only be one arg and it must be the folder that contains the solution or the csproj files");
			}
			var solutionFolder = args.Single();

			var csProjFiles = Directory.GetFiles(solutionFolder, "*.csproj", SearchOption.AllDirectories);

			foreach (var projectFile in csProjFiles)
			{
				ConvertProj(projectFile);
			}
			Console.WriteLine("All csproj files in the nested directories have been converted!.");
			Console.ReadKey();
		}



		public static void ConvertProj(string csprojPath)
		{
			if (!File.Exists(csprojPath))
			{
				throw new Exception($"{csprojPath} does not exist.");
			}
			Project proj = null;

			try
			{
				proj = new Project(csprojPath, null, null, ProjectCollection.GlobalProjectCollection, ProjectLoadSettings.IgnoreMissingImports);
			}
			catch(Exception ex)
			{
				var color = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"{Path.GetFileNameWithoutExtension(csprojPath)} is not in the .NET Core format already, it cannot be converted!");
				Console.ForegroundColor = color;
				return;
			}

			proj.TransformReferences()
				.TransformProjectType()
				.DeletePackageConfig()
				.RemoveCompileItems()
				.UpdateNoneItems()
				.AddRequiredProperties()
				.RemoveProjectReferenceGuids()
				.SaveProject()
				//Operate on file itself
				.SortProject()
				.RemoveXMLNamespace()
				.CleanPackageReferences();

			Console.WriteLine($"{Path.GetFileNameWithoutExtension(csprojPath)} has been converted to the new csproj format!");
		}


		//--------------------------------------------------------------------------------------------------------
		//-----------------------------------------Project Mutators-----------------------------------------------
		//--------------------------------------------------------------------------------------------------------

		private static Project TransformReferences(this Project proj)
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
				try
				{
					nonVersionDetail = splitDetails.IndexOf(splitDetails.First(x => x.All(c => char.IsNumber(c))));//Find the first full number Value between the .'s  Package.Name.*1*.0.0
				}
				catch//This is the case where it isn't a package, but it is a dll reference
				{
					continue;
				}

				string packageName = string.Join(".", splitDetails.Take(nonVersionDetail));
				string packageVersion = string.Join(".", splitDetails.Skip(nonVersionDetail));


				if (packagesToAdd.ContainsKey(packageName))
				{
					try
					{
						var existing = Semver.SemVersion.Parse(packagesToAdd[packageName]);
						var attempt = Semver.SemVersion.Parse(packageVersion);
						if (existing < attempt)
						{
							Console.WriteLine($"{packageName}.{packagesToAdd[packageName]} has been converted changed to {packageVersion} due to detection of a higher version being used.");
							packagesToAdd[packageName] = packageVersion;
						}

					}
					catch
					{
						ulong existing = ulong.Parse(packagesToAdd[packageName].Replace(".", ""));
						ulong attempt = ulong.Parse(packageVersion.Replace(".", ""));

						if (existing < attempt)
						{
							Console.WriteLine($"{packageName}.{packagesToAdd[packageName]} has been converted changed to {packageVersion} due to detection of a higher version being used.");
							packagesToAdd[packageName] = packageVersion;

						}
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

		private static Project DeletePackageConfig(this Project proj)
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

		private static Project TransformProjectType(this Project proj)
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

		private static Project AddRequiredProperties(this Project proj)
		{

			proj.SetProperty("RestoreProjectStyle", "PackageReference");
			proj.SetProperty("EnableDefaultEmbeddedResourceItems", "false");
			proj.SetProperty("AutoGenerateBindingRedirects", "true");
			proj.SetProperty("GenerateAssemblyInfo", "false");

			return proj;
		}

		private static Project SaveProject(this Project proj)
		{
			proj.Save();

			return proj;
		}

		/// <summary>
		/// https://github.com/miratechcorp/CsProjArrange
		/// </summary>
		/// <param name="proj"></param>
		/// <returns></returns>
		private static Project SortProject(this Project proj)
		{
			new CsProjArrangeConsole().Run(new string[] { $"--input={proj.ProjectFileLocation.LocationString}","--options=NoSortRootElements" });

			return proj;
		}

		private static Project RemoveXMLNamespace(this Project proj)
		{
			var fileData = File.ReadAllText(proj.ProjectFileLocation.LocationString);

			fileData = fileData.Replace(@" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003""", "")
				.Replace($@"<?xml version=""1.0"" encoding=""utf-8""?>{Environment.NewLine}", "");


			File.WriteAllText(proj.ProjectFileLocation.LocationString, fileData);

			proj.MarkDirty();

			return proj;
		}

		private static Project RemoveCompileItems(this Project proj)
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
		private static Project UpdateNoneItems(this Project proj)
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

		private static Project RemoveProjectReferenceGuids(this Project proj)
		{

			var projectRefs = proj.GetItems("ProjectReference").ToList();
			foreach(var pref in projectRefs)
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


		private static Project CleanPackageReferences(this Project proj)
		{
			var fileData = File.ReadAllText(proj.ProjectFileLocation.LocationString);

			List<dynamic> packages = new List<dynamic>();

			foreach(var item in proj.GetItems("PackageReference"))
			{
				packages.Add(new
				{
					Name = item.UnevaluatedInclude,
					Version = item.GetMetadataValue("Version"),
					XML =
$@"<PackageReference Include=""{item.UnevaluatedInclude}"">
      <Version>{item.GetMetadataValue("Version")}</Version>
    </PackageReference>"
				});
			}

			foreach(var item in packages)
			{
				var replacement = $@"<PackageReference Include=""{item.Name}"" Version=""{item.Version}""/>";
				fileData = fileData.Replace(item.XML, replacement);
			}


			File.WriteAllText(proj.ProjectFileLocation.LocationString, fileData);

			return proj;
		}
	}
}
