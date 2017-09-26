using Microsoft.Build.Evaluation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CsProjConversionTool
{
	public class ProjectFile : IDependentOn<ProjectFile>
	{
		public Project BackingClass { get; set; }
		public string Name { get; set; }

		public List<PackageReference> Packages
		{
			get
			{
				var packages = new List<PackageReference>();
				var prs = BackingClass?.GetItems("PackageReference")?.ToList() ?? new List<ProjectItem>();
				foreach (var item in prs)
				{
					var pr = new PackageReference
					{
						Name = item.UnevaluatedInclude,
						Version = item.GetMetadataValue("Version")
					};
					packages.Add(pr);
				}
				return packages;
			}
		}


		public ProjectFile(Project backingClass)
		{
			BackingClass = backingClass;
			Name = Path.GetFileNameWithoutExtension(BackingClass.ProjectFileLocation.File);


		}

		private List<string> _dependentOnProjects;
		public List<string> DependentOnProjects
		{
			get
			{
				if (_dependentOnProjects == null)
				{
					_dependentOnProjects = new List<string>();

					var prs = BackingClass.GetItems("ProjectReference").ToList();

					foreach (var item in prs)
					{
						_dependentOnProjects.Add(Path.GetFileNameWithoutExtension(item.UnevaluatedInclude));
					}
				}
				return _dependentOnProjects;
			}
		}

		public override string ToString()
		{
			return Name;
		}

		public bool DependentOn(ProjectFile possibleDependency)
		{
			return DependentOnProjects.Contains(possibleDependency.Name, StringComparer.CurrentCultureIgnoreCase);

		}
	}

	public class PackageReference
	{
		public string Name { get; set; }

		public string Version { get; set; }


		public override bool Equals(object obj)
		{
			if (obj is PackageReference pr)
			{
				return string.Equals(Name, pr.Name, StringComparison.CurrentCultureIgnoreCase)
					&& string.Equals(Version, pr.Version, StringComparison.CurrentCultureIgnoreCase);
			}
			return false;
		}

		public override int GetHashCode()
		{
			int inc = 7;
			int hash = 23;
			hash += Name.GetHashCode() * inc;
			hash += Version.GetHashCode() * inc;


			return base.GetHashCode();
		}
		public override string ToString()
		{
			return $"{Name} - V{Version}";
		}
	}
}
