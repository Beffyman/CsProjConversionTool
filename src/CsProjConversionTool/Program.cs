using CsProjArrange;
using System;
using Microsoft.Build.Evaluation;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using MoreLinq;
using NuGet.Versioning;

namespace CsProjConversionTool
{
	public static class Program
	{
		static int Main(string[] args)
		{
			if (args.Count() != 1)
			{
				WriteLine("There can only be one arg and it must be the folder that contains the solution or the csproj files", ConsoleColor.Red);
				Console.ReadKey();
				return -1;
			}
			var solutionFolder = args.Single();

			var csProjFiles = Directory.GetFiles(solutionFolder, "*.csproj", SearchOption.AllDirectories);

			List<ProjectFile> projects = csProjFiles.Select(x => ConvertProj(x)).Where(x => x != null).ToList();


			WriteLine("All csproj files in the nested directories have been converted!", ConsoleColor.Green);
			WriteLine("There will now be an attempt to resolve and simplify the dependencies within the projects!", ConsoleColor.Magenta);
			Thread.Sleep(1000);

			Corrections(projects);

			WriteLine("All projects have been simplified!", ConsoleColor.Green);
			WriteLine("There will now be an attempt to manually edit the files and clean them!", ConsoleColor.Magenta);
			Thread.Sleep(1000);
			projects.ForEach(x => FinalConversion(x));


			WriteLine("Conversion Tool has been ran to completion!", ConsoleColor.Green);
			Console.ReadKey();
			return 1;
		}



		public static ProjectFile ConvertProj(string csprojPath)
		{
			if (!File.Exists(csprojPath))
			{
				WriteLine($"{csprojPath} does not exist. It must have been inside the same folder as another project file.", ConsoleColor.DarkRed);
				return null;
			}
			Project proj = null;

			try
			{
				proj = new Project(csprojPath, null, null, ProjectCollection.GlobalProjectCollection, ProjectLoadSettings.IgnoreMissingImports);
			}
			catch (Exception)
			{
				WriteLine($"{Path.GetFileNameWithoutExtension(csprojPath)} is not in the .NET Core format already, it cannot be converted!", ConsoleColor.Yellow);
				return null;
			}

			proj.DeleteNonIncludedFiles()
				.TransformReferences()
				.AddTestPackageReference()
				.TransformProjectType()
				.DeletePackageConfig()
				.RemoveCompileItems()
				.UpdateNoneItems()
				.AddRequiredProperties()
				.RemoveProjectReferenceGuids()
				.SaveProject();

			WriteLine($"{Path.GetFileNameWithoutExtension(csprojPath)} has been converted to the new csproj format!");

			return new ProjectFile(proj);
		}


		public static void Corrections(IList<ProjectFile> projects)
		{
			//List<PackageReference> allReferences = projects.SelectMany(x => x.Packages).ToList();

			Dictionary<string, IList<string>> projectDependencies = new Dictionary<string, IList<string>>();

			foreach (var project in projects)
			{
				projectDependencies.Add(project.Name, project.DependentOnProjects);
			}

			NodeList<ProjectFile> nodes = new NodeList<ProjectFile>();

			foreach (var project in projectDependencies)
			{
				var projectWithDepns = projects.SingleOrDefault(x => x.Name == project.Key);

				if (projectWithDepns != null)
				{
					nodes.Add(new Node<ProjectFile>(projectWithDepns));

				}
				else
				{
					WriteLine($"Project {project.Key} does not exist within the solution and will not be simplified", ConsoleColor.Yellow);
				}

			}

			nodes.Map();//Map all dependencies together into a tree, check the UML ToString()

			var map = nodes.ToString();

			//Mutate current package references within a node dependency tree to update to the same version.
			bool upgradesFound = true;
			while (upgradesFound)
			{
				upgradesFound = false;
				foreach (var node in nodes)
				{
					List<PackageReference> packages = new List<PackageReference>();
					foreach (var child in node.DependentBy)
					{
						packages.AddRange(child.Value.Packages);
					}
					foreach (var child in node.DependentOn)
					{
						packages.AddRange(child.Value.Packages);
					}

					packages = packages.Distinct().ToList();

					var samePackages = packages.GroupBy(x => x.Name)
						.Select(x =>
							x.MaxBy(y => NuGetVersion.Parse(y.Version)))
						.ToList();
					foreach (var package in samePackages)
					{
						foreach (var child in node.DependentBy)
						{
							var version = child.Value.BackingClass.GetPackageVersion(package.Name);
							if (version != null && version != package.Version)
							{
								upgradesFound = true;
								child.Value.BackingClass.MutatePackageReference(package.Name, package.Version);
								WriteLine($"Project {child.Value.Name} will be updated to {package}", ConsoleColor.Yellow);
							}
						}
					}
				}
			}



			foreach (var proj in projects)
			{
				proj.BackingClass.SaveProject();
			}

		}

		public static void FinalConversion(ProjectFile proj)
		{
			proj.BackingClass.SortProject()
				.RemoveXMLNamespace()
				.CleanPackageReferences();
		}

		public static void WriteLine(string str, ConsoleColor color = ConsoleColor.White)
		{
			var conColor = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine(str);
			Console.ForegroundColor = color;
		}

	}
}
