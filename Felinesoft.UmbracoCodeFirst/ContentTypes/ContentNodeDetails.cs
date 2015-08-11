
using Umbraco.Core.Models;
namespace Felinesoft.UmbracoCodeFirst.ContentTypes
{
    public abstract class ContentNodeDetails<T> : ContentNodeDetails where T : IContentBase
    {
        public T Content { get; set; }

        public virtual void Initialise(T content, string contentTypeAlias)
        {
            Name = content.Name;
            Content = content;
            ContentTypeAlias = contentTypeAlias;
            UmbracoId = content.Id;
            SortOrder = content.SortOrder;
            Url = string.Empty;
        }
    }

    public abstract class ContentNodeDetails 
    {
        public virtual void Initialise(IPublishedContent content)
        {
            Name = content.Name;
            Url = content.Url;
            ContentTypeAlias = content.ContentType.Alias;
            UmbracoId = content.Id;
            SortOrder = content.SortOrder;
        }

        /// <summary>
        /// The name of the node
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The alias of the node type
        /// </summary>
        public string ContentTypeAlias { get; protected set; }

        /// <summary>
        /// The id of the node
        /// </summary>
        public int UmbracoId { get; protected set; }

        public int SortOrder { get; set; }

        public string Url { get; protected set; }
    }
}