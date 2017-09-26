using CsProjArrange;
using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CsProjConversionTool
{
	public static class ProjectFileMutators
	{

		public static Project CleanPackageReferences(this Project proj)
		{
			var fileData = File.ReadAllText(proj.ProjectFileLocation.LocationString);

			List<dynamic> packages = new List<dynamic>();

			foreach (var item in proj.GetItems("PackageReference"))
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

			foreach (var item in packages)
			{
				var replacement = $@"<PackageReference Include=""{item.Name}"" Version=""{item.Version}""/>";
				fileData = fileData.Replace(item.XML, replacement);
			}


			File.WriteAllText(proj.ProjectFileLocation.LocationString, fileData);
			Program.WriteLine($"{proj.ProjectFileLocation.File} has had its PackageReferences cleaned.");
			return proj;
		}

		/// <summary>
		/// https://github.com/miratechcorp/CsProjArrange
		/// </summary>
		/// <param name="proj"></param>
		/// <returns></returns>
		public static Project SortProject(this Project proj)
		{
			new CsProjArrangeConsole().Run(new string[] { $"--input={proj.ProjectFileLocation.LocationString}", "--options=NoSortRootElements" });
			Program.WriteLine($"{proj.ProjectFileLocation.File} has been sorted.");

			return proj;
		}

		public static Project RemoveXMLNamespace(this Project proj)
		{
			var fileData = File.ReadAllText(proj.ProjectFileLocation.LocationString);

			fileData = fileData.Replace(@" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003""", "")
				.Replace($@"<?xml version=""1.0"" encoding=""utf-8""?>{Environment.NewLine}", "");


			File.WriteAllText(proj.ProjectFileLocation.LocationString, fileData);

			proj.MarkDirty();
			Program.WriteLine($"{proj.ProjectFileLocation.File} had its headers removed.");

			return proj;
		}

	}
}
