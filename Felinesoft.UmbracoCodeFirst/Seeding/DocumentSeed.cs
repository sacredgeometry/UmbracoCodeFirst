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
	public sealed class DocumentSeed : Seed<DocumentTypeBase>
	{
		internal DocumentSeed(string nodeName, DocumentTypeBase document, IEnumerable<DocumentSeed> children)
			: base(nodeName, document)
		{
			Children = children;
		}

		public IEnumerable<DocumentSeed> Children { get; private set; }
	}
}