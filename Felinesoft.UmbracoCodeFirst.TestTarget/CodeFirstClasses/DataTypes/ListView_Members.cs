
using Felinesoft.UmbracoCodeFirst;
using Felinesoft.UmbracoCodeFirst.ContentTypes;
using Felinesoft.UmbracoCodeFirst.DataTypes;
using Felinesoft.UmbracoCodeFirst.DataTypes.BuiltIn;
using Felinesoft.UmbracoCodeFirst.Attributes;
using Felinesoft.UmbracoCodeFirst.Extensions;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Umbraco.Core.Models;
using System;

namespace LMI.BusinessLogic.CodeFirst
{
    [DataType("Umbraco.ListView", "List View - Members")]
    [PreValue("pageSize", @"10")]
    [PreValue("orderBy", @"Name")]
    [PreValue("orderDirection", @"asc")]
    [PreValue("includeProperties", @"[{""alias"":""email"",""isSystem"":1},{""alias"":""username"",""isSystem"":1},{""alias"":""updateDate"",""header"":""Last edited"",""isSystem"":1}]")]
    public class ListView_Members : IUmbracoNvarcharDataType
    {
        //TODO implement the properties and serialisation logic for the Umbraco.ListView property editor's values

        /// <summary>
        /// Initialises the instance from the db value
        /// </summary>
        public void Initialise(string dbValue)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Serialises the instance to the db value
        /// </summary>
        public string Serialise()
        {
            throw new NotImplementedException();
        }
    }
}