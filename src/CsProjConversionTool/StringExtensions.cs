using System;
using System.Collections.Generic;
using System.Text;

namespace CsProjConversionTool
{
	public static class StringExtensions
	{

		public static string GetParent(this string str)
		{
			return str.Contains(".") ? str.Split(".")[0] : str;
		}
	}
}
