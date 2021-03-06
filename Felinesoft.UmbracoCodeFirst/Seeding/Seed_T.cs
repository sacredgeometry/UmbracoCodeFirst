using System;
using System.Linq;
using System.Collections.Generic;
using Felinesoft.UmbracoCodeFirst.ContentTypes;
using Felinesoft.UmbracoCodeFirst.Core.Resolver;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;

namespace Felinesoft.UmbracoCodeFirst.Seeding
{

	public abstract class Seed<T> : Seed where T : CodeFirstContentBase
	{
		protected Seed(string nodeName, T content)
		{
			NodeName = nodeName;
			Content = content;
		}

		public string NodeName { get; private set; }

		public T Content { get; private set; }
	}

}