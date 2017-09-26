using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CsProjConversionTool
{

	public interface IDependentOn<T>
	{
		bool DependentOn(T possibleDependency);
	}

	public class Node<T> where T : IDependentOn<T>
	{
		public T Value;

		/// <summary>
		/// Nodes that this node requires
		/// </summary>
		public IList<Node<T>> DependentOn { get; set; }

		/// <summary>
		/// Nodes that require this node
		/// </summary>
		public IList<Node<T>> DependentBy { get; set; }

		public Node(T value)
		{
			Value = value;
			DependentOn = new List<Node<T>>();
			DependentBy = new List<Node<T>>();
		}


		public override string ToString()
		{
			return Value.ToString();
		}
	}


	public class NodeList<T> : List<Node<T>> where T : IDependentOn<T>
	{

		public void Map()
		{
			foreach(var parentNode in this)
			{
				foreach(var childNode in this)
				{
					if (parentNode.Value.DependentOn(childNode.Value))
					{
						parentNode.DependentOn.Add(childNode);
						childNode.DependentBy.Add(parentNode);
					}
				}
			}

			foreach(var node in this)
			{
				node.DependentBy = node.DependentBy.Distinct().ToList();
				node.DependentOn = node.DependentOn.Distinct().ToList();
			}
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();

			sb.AppendLine("@startuml");

			//foreach(var node in this)
			//{
			//	sb.AppendLine($"[{node.Value.ToString()}]");
			//}

			List<KeyValuePair<string, string>> maps = new List<KeyValuePair<string, string>>();

			foreach(var nodeGroup in this.GroupBy(x=>x.Value.ToString().GetParent()))
			{
				sb.AppendLine($"node \"Group_{nodeGroup.Key}\" {{");

				foreach(var node in nodeGroup)
				{
					sb.AppendLine($"[{node.Value.ToString()}]");
					foreach (var dependentBy in node.DependentBy)
					{
						maps.Add(new KeyValuePair<string, string>(dependentBy.Value.ToString(), node.Value.ToString()));
					}
					foreach (var dependentOn in node.DependentOn)
					{
						maps.Add(new KeyValuePair<string, string>(node.Value.ToString(), dependentOn.Value.ToString()));
					}
				}
				sb.AppendLine($"}}");
			}
			maps = maps.Distinct().ToList();
			foreach (var map in maps)
			{
				sb.AppendLine($"[{map.Key}] <-down- [{map.Value}]");
			}

			sb.AppendLine("@enduml");



			return sb.ToString();
		}

	}

}
