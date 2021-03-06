﻿using Felinesoft.UmbracoCodeFirst.Attributes;
using Felinesoft.UmbracoCodeFirst.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Models;
using Felinesoft.UmbracoCodeFirst.Extensions;
using Umbraco.Core.Exceptions;
using Felinesoft.UmbracoCodeFirst.Core.Resolver;
using Umbraco.Web.Models.Trees;
using Felinesoft.UmbracoCodeFirst.Core.ClassFileGeneration;
using Felinesoft.UmbracoCodeFirst.Core.Modules.ContentTypeBase.T4;
using System.IO;
using Felinesoft.UmbracoCodeFirst.Diagnostics;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using System.Web;
using Umbraco.Web;
using System.Text.RegularExpressions;

namespace Felinesoft.UmbracoCodeFirst.Core.Modules
{
    public abstract class ContentTypeModuleBase : IContentTypeModuleBase, IEntityTreeFilter
    {
        private Lazy<IEnumerable<IContentTypeBase>> _allContentTypes;
        private IPropertyModule _propertyModule;
        private ContentTypeRegister _register;
        private ContentTypeRegister.ContentTypeRegisterController _controller;
        private Lazy<List<ContentTypeRegistration>> _typesUsedInCompositions;
        private Guid _performanceTimer;

        protected ContentTypeModuleBase(IPropertyModule propertyModule, Guid performanceTimer)
        {
            _performanceTimer = performanceTimer;
            _propertyModule = propertyModule;
            _register = new ContentTypeRegister(out _controller);
            _typesUsedInCompositions = new Lazy<List<ContentTypeRegistration>>(() =>
            {
                var usedInCompositions = new List<ContentTypeRegistration>(_register.Registrations.SelectMany(x => GetComposingTypes(x, null)));
                usedInCompositions.AddRange(_register.Registrations.SelectMany(x => GetAllAncestorTypes(x)));
                usedInCompositions = usedInCompositions.Distinct().ToList();
                return usedInCompositions;
            });
            ResetContentTypesCache();
        }

        #region IContentTypeModuleBase
        public bool TryGetContentType(string alias, out ContentTypeRegistration registration)
        {
            try
            {
                registration = GetRegistration(alias);
                return true;
            }
            catch
            {
                registration = null;
                return false;
            }
        }

        public bool TryGetContentType(Type type, out ContentTypeRegistration registration)
        {
            try
            {
                registration = GetRegistration(type);
                return true;
            }
            catch
            {
                registration = null;
                return false;
            }
        }
        #endregion

        #region Content Type Service Adapter Abstracts
        protected abstract IEnumerable<IContentTypeBase> GetAllContentTypes();

        protected void Save(IContentTypeBase contentType)
        {
            CodeFirstManager.Current.Log("Attempting to save changes to content type " + contentType.Name, this);
            if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Ensure)
            {
                CodeFirstManager.Current.Log("ERROR: In Ensure mode and the following content type is not sync'ed: " + contentType.Name, this);
                throw new CodeFirstPassiveInitialisationException("The types defined in the database do not match the types passed in to initialise. In InitialisationMode.Ensure the types must match or the site will be prevented from starting.");
            }
            else if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Sync)
            {
                SaveContentType(contentType);
                CodeFirstManager.Current.Log("Saved changes to content type " + contentType.Name, this);
            }
            else if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Passive)
            {
                CodeFirstManager.Current.Log("In passive mode - ignoring changes to content type " + contentType.Name, this);
                return;
            }
            else
            {
                throw new CodeFirstException("Unknown initialisation mode");
            }
        }

        protected void Delete(IContentTypeBase contentType)
        {
            if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Ensure)
            {
                throw new CodeFirstPassiveInitialisationException("The types defined in the database do not match the types passed in to initialise. In InitialisationMode.Ensure the types must match or the site will be prevented from starting.");
            }
            else if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Sync)
            {
                DeleteContentType(contentType);
            }
            else if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Passive)
            {
                return;
            }
            else
            {
                throw new CodeFirstException("Unknown initialisation mode");
            }
        }

        protected abstract void SaveContentType(IContentTypeBase contentType);

        protected abstract void DeleteContentType(IContentTypeBase contentType);

        protected abstract IEnumerable<IContentTypeBase> GetChildren(IContentTypeBase contentType);

        protected abstract IContentTypeComposition GetContentTypeByAlias(string alias);

        protected abstract IContentTypeComposition CreateContentType(IContentTypeBase parent);

        protected abstract ContentTypeRegistration CreateRegistration(Type type);
        #endregion

        #region Execution Helpers
        protected void DoPerType(IEnumerable<Type> types, Action<Type> action, bool resetCacheBeforeAndAfter = true)
        {
            if (resetCacheBeforeAndAfter)
            {
                ResetContentTypesCache();
            }
            foreach (var type in types)
            {
                action.Invoke(type);
            }
            if (resetCacheBeforeAndAfter)
            {
                ResetContentTypesCache();
            }
        }

        protected void DoPerTypeConcurrent(IEnumerable<Type> types, Action<Type> action, bool resetCacheBeforeAndAfter = true)
        {
            if (resetCacheBeforeAndAfter)
            {
                ResetContentTypesCache();
            }
            List<System.Threading.Tasks.Task> tasks = new List<System.Threading.Tasks.Task>();

            foreach (var type in types)
            {
                tasks.Add(System.Threading.Tasks.Task.Run(() =>
                    {
                        action.Invoke(type);
                    }));
            }
            System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
            if (resetCacheBeforeAndAfter)
            {
                ResetContentTypesCache();
            }
        }
        #endregion

        #region Umbraco Content Types Cache
        protected IEnumerable<IContentTypeBase> AllContentTypes
        {
            get
            {
                return _allContentTypes.Value;
            }
        }

        protected void ResetContentTypesCache()
        {
            _allContentTypes = new Lazy<IEnumerable<IContentTypeBase>>(() => GetAllContentTypes());
        }
        #endregion

        #region Registration
        protected ContentTypeRegister ContentTypeRegister { get { return _register; } }

        protected void Register(ContentTypeRegistration registration)
        {
            _controller.Register(registration);
        }

        protected ContentTypeRegistration GetRegistration(string alias)
        {
            return ContentTypeRegister.GetContentType(alias);
        }

        protected ContentTypeRegistration GetRegistration(Type type)
        {
            return ContentTypeRegister.GetContentType(type);
        }
        #endregion

        #region AllowedChildren
        protected virtual void SyncAllowedChildren(ContentTypeRegistration docTypeReg)
        {
            CodeFirstManager.Current.Log("Syncing allowed children for content type " + docTypeReg.Name, this);
            bool modified = false;
            var allowedChildren = FetchAllowedContentTypes(docTypeReg.ContentTypeAttribute.AllowedChildren, docTypeReg.ClrType);
            var type = GetContentTypeByAlias(docTypeReg.Alias);
            if (type == null)
            {
                throw new CodeFirstException(docTypeReg.Name + " (alias: " + docTypeReg.Alias + ") is not found or is an entity type which does not implement IContentTypeBase");
            }

			//REMOVED - Dan M 04/01/2016 - this is now checked in FetchAllowedContentTypes (or should be...)
			//Check that all specified allowed children are registered as CF types
			//foreach (var child in allowedChildren)
			//{
			//	ContentTypeRegistration childReg;
			//	if (!TryGetContentType(child.Alias, out childReg))
			//	{
			//		throw new CodeFirstException(string.Format("Type with alias {0} was specified as an allowed child of type with alias {1}, but no type with alias {0} is registered in code-first", child.Alias, type.Alias));
			//	}
			//}

			//Flag as modified if any existing children need to be removed
			foreach (var child in type.AllowedContentTypes)
            {
                if (!allowedChildren.Any(x => x.Alias == child.Alias))
                {
                    modified = true;
                    break;
                }
            }

			//If we haven't already detected any mods, check if any new allowed children have been added
            if (!modified)
            {
                foreach (var child in allowedChildren)
                {
                    if (!type.AllowedContentTypes.Any(x => x.Alias == child.Alias))
                    {
                        modified = true;
                        break;
                    }
                }
            }

            if (modified)
            {
                type.AllowedContentTypes = allowedChildren;
                type.ResetDirtyProperties(false);
                Save(type);
            }
        }
        #endregion

        #region Composition
        protected virtual void SyncCompositions(ContentTypeRegistration docTypeReg)
        {
            CodeFirstManager.Current.Log("Syncing compositions for content type " + docTypeReg.Name, this);
            bool modified = false;
            var type = GetContentTypeByAlias(docTypeReg.Alias);
            if (type == null)
            {
                throw new CodeFirstException(docTypeReg.Name + " (alias: " + docTypeReg.Alias + ") is not found or is an entity type which does not implement IContentTypeComposition");
            }
            var comps = GetComposingTypes(docTypeReg, _typesUsedInCompositions.Value);
            var toRemove = new List<IContentTypeComposition>();

            foreach (var comp in type.ContentTypeComposition)
            {
                ContentTypeRegistration compReg;
                if (!TryGetContentType(comp.Alias, out compReg) || (!docTypeReg.ClrType.Inherits(compReg.ClrType) && !comps.Contains(compReg)))
                {
                    //Not a registered code-first type or not an ancestor and not a defined composition. Remove.                 
                    toRemove.Add(comp);
                }
            }
            foreach (var comp in toRemove)
            {
                modified = true;
                CodeFirstManager.Current.Log("Removing composition with " + comp.Name + " from " + docTypeReg.Name, this);
                type.RemoveContentType(comp.Alias);
            }
            foreach (var comp in comps)
            {
                if (!type.ContentTypeCompositionExists(comp.Alias))
                {
                    modified = true;
                    CodeFirstManager.Current.Log("Adding composition with " + comp.Name + " to " + docTypeReg.Name, this);
                    var compType = GetContentTypeByAlias(comp.Alias);
                    try
                    {
                        type.AddContentType(compType);
                    }
                    catch (InvalidCompositionException ex)
                    {
                        throw new CodeFirstException("Invalid Composition - " + ex.Message, ex);
                    }
                }
            }

            docTypeReg._compositions = comps;

            if (modified)
            {
                Save(type);
            }
        }

        private List<ContentTypeCompositionRegistration> GetComposingTypes(ContentTypeRegistration docTypeReg, List<ContentTypeRegistration> usedInCompositions)
        {
            var result = new List<ContentTypeCompositionRegistration>();
            var compositionProperties = docTypeReg.ClrType.GetProperties().Where(x => x.GetCodeFirstAttribute<ContentCompositionAttribute>() != null);
            var targetBases = GetAllAncestorTypes(docTypeReg);

            foreach (var comp in compositionProperties)
            {
                ContentTypeRegistration compReg;
                if (TryGetContentType(comp.PropertyType, out compReg))
                {
                    ValidateComposition(docTypeReg, targetBases, comp, compReg, usedInCompositions);
                    result.Add(new ContentTypeCompositionRegistration(compReg, comp));
                }
                else
                {
                    throw new CodeFirstException("Property " + comp.Name + " has type " + comp.PropertyType.Name + " which is not a registered document type and cannot be used as a composition on document type " + docTypeReg.ClrType.Name);
                }
            }
            return result;
        }

        private void ValidateComposition(ContentTypeRegistration docTypeReg, List<ContentTypeRegistration> targetBases, PropertyInfo comp, ContentTypeRegistration compReg, List<ContentTypeRegistration> usedInCompositions)
        {
            if (compReg.ClrType == docTypeReg.ClrType) //validate compose with self
            {
                throw new CodeFirstException(docTypeReg.ClrType.Name + " declares a composition with itself. Property name: " + comp.Name);
            }
            else if (docTypeReg.ClrType.Inherits(compReg.ClrType)) //validate compose with ancestor
            {
                throw new CodeFirstException(docTypeReg.ClrType.Name + " declares a composition with an ancestor. Property name: " + comp.Name + ", Ancestor type: " + compReg.ClrType.Name);
            }
            else if (compReg.ClrType.Inherits(docTypeReg.ClrType)) //validate compose with descendant
            {
                throw new CodeFirstException(docTypeReg.ClrType.Name + " declares a composition with a descendant. Property name: " + comp.Name + ", Descendant type: " + compReg.ClrType.Name);
            }
            else if (usedInCompositions != null && usedInCompositions.Contains(docTypeReg)) //validate compose of type which is used in compositions
            {
                throw new CodeFirstException(docTypeReg.ClrType.Name + " declares a composition but is used in other compositions or is used as a parent. Therefore it cannot be composed itself.");
            }
            else //validate compose with common ancestors
            {
                var sourceBases = GetAllAncestorTypes(compReg);
                var ancestors = sourceBases.Intersect(targetBases);
                if (ancestors.Any())
                {
                    var ancestorString = string.Join(", ", ancestors.Select(x => x.ClrType.Name));
                    throw new CodeFirstException(docTypeReg.ClrType.Name + " declares an invalid composition with " + compReg.ClrType.Name + ". The types have the following common ancestors: " + ancestorString);
                }
            }
        }

        private List<ContentTypeRegistration> GetAllAncestorTypes(ContentTypeRegistration docTypeReg)
        {
            var targetBases = new List<ContentTypeRegistration>();
            var targetBase = docTypeReg.ClrType.BaseType;
            while (targetBase != null)
            {
                ContentTypeRegistration reg;
                if (TryGetContentType(targetBase, out reg))
                {
                    targetBases.Add(reg);
                }
                targetBase = targetBase.BaseType;
            }
            return targetBases;
        }
        #endregion

        #region Content Types
        public virtual void Initialise(IEnumerable<Type> types)
        {
            if (_performanceTimer != Guid.Empty) 
                Timing.StartTimer(_performanceTimer, this.GetType().Name + " Module Initialise", "Sorting types by inheritance");
            var sortedTypes = types.GetGenerationsByContentTypeInheritance();

            if (_performanceTimer != Guid.Empty) 
                Timing.MarkTimer(_performanceTimer, "Starting Content Type Sync for module " + this.GetType().Name);
            CodeFirstManager.Current.Log("Initialising content types", this);
            foreach (var generation in sortedTypes)
            {
                SyncContentTypes(generation);
            }

            if (CodeFirstManager.Current.Features.InitialisationMode == InitialisationMode.Passive)
            {
                if (_performanceTimer != Guid.Empty)
                    Timing.EndTimer(_performanceTimer, "Complete - skipped allowed children and composition sync as InitialisationMode is Passive");
                return;
            }

            if (_performanceTimer != Guid.Empty) 
                Timing.MarkTimer(_performanceTimer, "Starting Allowed Children and Composition Sync");
            CodeFirstManager.Current.Log("Starting Allowed Children and Composition Sync", this);
            SyncChildrenAndCompositions(sortedTypes);

            if (_performanceTimer != Guid.Empty)
                Timing.EndTimer(_performanceTimer, "Complete");
        }

        private void SyncContentTypes(List<Type> generation)
        {
            Action<Type> action = type =>
            {
                if (_performanceTimer != Guid.Empty)
                    Timing.MarkTimer(_performanceTimer, "Staring " + type.Name + " Content Type Sync");
                var registration = CreateRegistration(type);
                CreateOrUpdateContentType(registration);
                Register(registration);
            };

            if (CodeFirstManager.Current.Features.UseConcurrentInitialisation)
            {
                DoPerTypeConcurrent(generation, action);
            }
            else
            {
                DoPerType(generation, action);
            }
        }

        private void SyncChildrenAndCompositions(List<List<Type>> sortedTypes)
        {
            Action<Type> action = type =>
            {
                var contentTypeReg = ContentTypeRegister.GetContentType(type);
                if (contentTypeReg.ContentTypeAttribute != null)
                {
                    if (_performanceTimer != Guid.Empty)
                        Timing.MarkTimer(_performanceTimer, "Starting " + type.Name + " Allowed Children Sync");

                    if (contentTypeReg.ContentTypeAttribute.AllowedChildren == null)
                    {
                        contentTypeReg.ContentTypeAttribute.AllowedChildren = new Type[] { };
                    }

                    SyncAllowedChildren(contentTypeReg);
                }
                if (_performanceTimer != Guid.Empty)
                    Timing.MarkTimer(_performanceTimer, "Starting " + type.Name + " Composition Sync");
                SyncCompositions(contentTypeReg);
            };

            if (CodeFirstManager.Current.Features.UseConcurrentInitialisation)
            {
                DoPerTypeConcurrent(sortedTypes.SelectMany(x => x), action);
            }
            else
            {
                DoPerType(sortedTypes.SelectMany(x => x), action);
            }
        }

        public virtual void CreateOrUpdateContentType(ContentTypeRegistration registration)
        {
            bool modified;
            IContentTypeBase contentType;
            if (!AllContentTypes.Any(x => x != null && string.Equals(x.Alias, registration.ContentTypeAttribute.Alias, StringComparison.InvariantCultureIgnoreCase)))
            {
                contentType = CreateContentType(registration, out modified);   
            }
            else
            {
                contentType = UpdateContentType(registration, out modified);
            }
            if (modified)
            {
                contentType.ResetDirtyProperties(false);
                Save(contentType);
            }
        }

        /// <summary>
        /// This method is called when the Content Type declared in the attribute hasn't been found in Umbraco
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="fileService"></param>
        /// <param name="attribute"></param>
        /// <param name="contentClrType"></param>
        /// <param name="dataTypeService"></param>
        protected virtual IContentTypeBase CreateContentType(ContentTypeRegistration registration, out bool modified)
        {
            CodeFirstManager.Current.Log("Creating content type for " + registration.ClrType.FullName, this);
            IContentTypeBase newContentType;
            modified = true; //always modify when we create (overriders may save and set this to false before returning though)

            ContentTypeAttribute parentAttribute;
            Type parentType;
            GetCodeFirstParent(registration.ClrType, out parentAttribute, out parentType);
            if (parentType != null && parentAttribute != null)
            {

                string parentAlias = parentAttribute.Alias;
                IContentTypeBase parentContentType = GetContentTypeByAlias(parentAlias);
                newContentType = CreateContentType(parentContentType);
            }
            else
            {
                IContentTypeBase fake = null;
                newContentType = CreateContentType(fake);
            }

            newContentType.Name = registration.ContentTypeAttribute.Name;
            newContentType.Alias = registration.ContentTypeAttribute.Alias;
            newContentType.Icon = registration.ContentTypeAttribute.Icon;
            newContentType.Description = registration.ContentTypeAttribute.Description;
            newContentType.AllowedAsRoot = registration.ContentTypeAttribute.AllowedAtRoot;
            newContentType.IsContainer = registration.ContentTypeAttribute.EnableListView;
            newContentType.AllowedContentTypes = new ContentTypeSort[0];

            //create tabs
            CreateTabs(newContentType, registration._tabs, registration.ClrType);

            //create properties on the generic tab
            var propertiesOfRoot = registration.ClrType.GetProperties().Where(x => x.GetCodeFirstAttribute<ContentPropertyAttribute>() != null);
            foreach (var item in propertiesOfRoot)
            {
                var reg = _propertyModule.CreateProperty(newContentType, null, item, registration.ClrType);
                registration._properties.Add(reg);
            }
            return newContentType;
        }

        /// <summary>
        /// Update the existing content Type based on the data in the attributes
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="attribute"></param>
        /// <param name="contentType"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        protected virtual IContentTypeBase UpdateContentType(ContentTypeRegistration registration, out bool modified)
        {
            CodeFirstManager.Current.Log("Syncing content type for " + registration.ClrType.FullName, this);
            var contentType = GetContentTypeByAlias(registration.ContentTypeAttribute.Alias);
            modified = false;

            //Get parent info
            ContentTypeAttribute parentAttribute;
            Type parentType;
            GetCodeFirstParent(registration.ClrType, out parentAttribute, out parentType);
            var parentContent = parentAttribute == null ? null : GetContentTypeByAlias(parentAttribute.Alias);
            var oldParent = AllContentTypes.FirstOrDefault(x => x.Id == contentType.ParentId);
            int targetParentId = parentContent == null ? -1 : parentContent.Id;

            //Check if parent has changed
            if (contentType.ParentId != targetParentId)
            {
                contentType = Reparent(registration, contentType, parentContent, oldParent);
            }

            var existingChildrenAliases = contentType.AllowedContentTypes.Select(x => x.Alias);
            modified = !contentType.Name.Equals(registration.ContentTypeAttribute.Name, StringComparison.InvariantCultureIgnoreCase) ||
                       !contentType.Alias.Equals(registration.ContentTypeAttribute.Alias, StringComparison.InvariantCultureIgnoreCase) ||
                       !contentType.Icon.Equals(registration.ContentTypeAttribute.Icon, StringComparison.InvariantCultureIgnoreCase) ||
                       contentType.IsContainer != registration.ContentTypeAttribute.EnableListView ||
                       contentType.AllowedAsRoot != registration.ContentTypeAttribute.AllowedAtRoot;

            if (contentType.Description != registration.ContentTypeAttribute.Description)
            {
                //If not both null/empty
                if (!(string.IsNullOrEmpty(contentType.Description) && string.IsNullOrEmpty(registration.ContentTypeAttribute.Description)))
                {
                    modified = true;
                }
            }

            contentType.Name = registration.ContentTypeAttribute.Name;
            contentType.Alias = registration.ContentTypeAttribute.Alias;
            contentType.Icon = registration.ContentTypeAttribute.Icon;
            contentType.Description = registration.ContentTypeAttribute.Description;
            contentType.IsContainer = registration.ContentTypeAttribute.EnableListView;
            contentType.AllowedAsRoot = registration.ContentTypeAttribute.AllowedAtRoot;

            VerifyProperties(contentType, registration.ClrType, registration._properties, registration._tabs, ref modified);

            //verify if a tab has no properties, if so remove
            //NB need to protect against removal of tabs declared on composition or ancestor types (note that ancestors show up in the composition collections so only that check is needed)
            var localPropertyGroups = contentType.PropertyGroups.Where(x => !contentType.CompositionPropertyGroups.Contains(x)).ToArray();
            var protectedTabs = registration.ClrType.GetCustomAttributes<DoNotRemoveTabAttribute>(true).Select(x => x.TabName);
            int length = localPropertyGroups.Length;
            for (int i = 0; i < length; i++)
            {
                if (localPropertyGroups[i].PropertyTypes.Count == 0 && !protectedTabs.Any(x => localPropertyGroups[i].Name.Equals(x, StringComparison.InvariantCultureIgnoreCase)))
                {
                    modified = true;
                    //remove
                    contentType.RemovePropertyGroup(localPropertyGroups[i].Name);
                }
            }

            if (modified)
            {
                CheckBuiltIn(registration);
            }

            return contentType;
        }

        private IContentTypeComposition Reparent(ContentTypeRegistration registration, IContentTypeComposition contentType, IContentTypeBase newParent, IContentTypeBase oldParent)
        {
            //TODO sort out reparenting
            throw new CodeFirstException("Changing parent types of existing content types is not supported. Consider dropping & recreating your content type hierarchy or using compositions instead." + Environment.NewLine +
                "Affected type alias: " + registration.Alias + ", previous parent alias: " + (oldParent == null ? "[none]" : oldParent.Alias) + ", new parent alias: " + (newParent == null ? "[none]" : newParent.Alias));
        }

        private void CheckBuiltIn(ContentTypeRegistration registration)
        {
            if (registration.ClrType.GetCustomAttribute<BuiltInTypeAttribute>(false) != null)
            {
                throw new CodeFirstException("A default type has been modified in the database. If you intend to modify the default types " +
                                             "you must switch off the equivalent code-first built-in types using the relevant property in " +
                                             "CodeFirstManager.Current.Features." + Environment.NewLine +
                                             "Affected type: " + registration.ClrType.Name + Environment.NewLine +
                                             "Built-in type category: " + registration.ClrType.GetCustomAttribute<BuiltInTypeAttribute>(false).BuiltInTypeName);
            }
        }

        private void GetCodeFirstParent(Type type, out ContentTypeAttribute parentAttribute, out Type parentType)
        {
            parentAttribute = null;
            parentType = type;
            while (parentType != null && parentAttribute == null)
            {
                parentType = parentType.BaseType;
                if (parentType != null)
                {
                    parentAttribute = parentType.GetCodeFirstAttribute<ContentTypeAttribute>();
                }
            }
        }
        #endregion

        #region Tabs
        /// <summary>
        /// Scans for properties on the model which have the UmbracoTab attribute
        /// </summary>
        /// <param name="newContentType"></param>
        /// <param name="model"></param>
        /// <param name="dataTypeService"></param>
        private void CreateTabs(IContentTypeBase newContentType, List<TabRegistration> tabRegister, Type contentClrType)
        {
            var properties = contentClrType.GetProperties().Where(x => x.GetCodeFirstAttribute<ContentTabAttribute>() != null).ToArray();
            int length = properties.Length;

            for (int i = 0; i < length; i++)
            {
                var tabAttribute = properties[i].GetCodeFirstAttribute<ContentTabAttribute>();
                var reg = new TabRegistration();
                var props = new List<PropertyRegistration>();
                reg._properties = props;
                reg.ClrType = properties[i].PropertyType;
                reg.Name = tabAttribute.Name;
                reg.OriginalName = tabAttribute.OriginalName;
                reg.TabAttribute = tabAttribute;
                reg.PropertyOfParent = properties[i];

                CodeFirstManager.Current.Log("Creating tab " + tabAttribute.Name + " on content type " + newContentType.Name, this);
                newContentType.AddPropertyGroup(tabAttribute.Name);
                newContentType.PropertyGroups.Where(x => x.Name == tabAttribute.Name).FirstOrDefault().SortOrder = tabAttribute.SortOrder;
                CreateProperties(properties[i], newContentType, reg, props, contentClrType);

                tabRegister.Add(reg);
            }
        }
        #endregion

        #region Properties
        /// <summary>
        /// Every property on the Tab object is scanned for the UmbracoProperty attribute
        /// </summary>
        /// <param name="propertyInfo"></param>
        /// <param name="newContentType"></param>
        /// <param name="tabName"></param>
        /// <param name="dataTypeService"></param>
        /// <param name="atTabGeneric"></param>
        private void CreateProperties(PropertyInfo propertyInfo, IContentTypeBase newContentType, TabRegistration tab, List<PropertyRegistration> propertyRegister, Type documentClrType)
        {
            Type type = propertyInfo.PropertyType;
            var properties = type.GetProperties().Where(x => x.GetCustomAttribute<ContentPropertyAttribute>() != null); //Do NOT initialise attributes here, causes exception
            if (properties.Count() > 0)
            {
                foreach (var item in properties)
                {
                    var reg = _propertyModule.CreateProperty(newContentType, tab, item, documentClrType);
                    propertyRegister.Add(reg);
                }
            }
        }

        /// <summary>
        /// Loop through all properties and remove existing ones if necessary
        /// </summary>
        /// <param name="contentType"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private void VerifyProperties(IContentTypeBase contentType, Type documentClrType, List<PropertyRegistration> propertyRegister, List<TabRegistration> tabRegister, ref bool modified)
        {
            var tabProperties = documentClrType.GetProperties().Where(x => x.GetCodeFirstAttribute<ContentTabAttribute>() != null).ToArray();
            var propertiesOfRoot = documentClrType.GetProperties().Where(x => x.GetCodeFirstAttribute<ContentPropertyAttribute>() != null);
            
            //Remove any properties which aren't in the CF definition
            Prune(contentType, documentClrType, tabProperties, propertiesOfRoot);

            foreach (var tabProperty in tabProperties)
            {
                var tabAttribute = tabProperty.GetCodeFirstAttribute<ContentTabAttribute>();
                var tabReg = new TabRegistration();
                tabReg.TabAttribute = tabAttribute;
                tabReg.Name = tabAttribute.Name;
                tabReg.OriginalName = tabAttribute.OriginalName;
                tabReg.PropertyOfParent = tabProperty;
                CodeFirstManager.Current.Log("Syncing tab " + tabReg.Name + " on content type " + contentType.Name, this);
                VerifyAllPropertiesOnTab(tabProperty, contentType, tabReg, documentClrType, ref modified);
                tabRegister.Add(tabReg);
            }

            foreach (var item in propertiesOfRoot)
            {
                CodeFirstManager.Current.Log("Syncing property " + item.Name + " on content type " + contentType.Name, this);
                var reg = _propertyModule.VerifyExistingProperty(contentType, null, item, documentClrType, ref modified);
                propertyRegister.Add(reg);
            }
        }

        private void Prune(IContentTypeBase contentType, Type documentClrType, PropertyInfo[] tabProperties, IEnumerable<PropertyInfo> propertiesOfRoot)
        {
            bool modified = false;

            var propertiesToKeep = propertiesOfRoot.Where(x =>
            {
                var attr = x.DeclaringType.GetCodeFirstAttribute<CodeFirstCommonBaseAttribute>(false);
                return x.DeclaringType == documentClrType || attr != null;
            }).Select(x => x.GetCodeFirstAttribute<ContentPropertyAttribute>().Alias)
            .Union(tabProperties.Where(x =>
            {
                var attr = x.DeclaringType.GetCodeFirstAttribute<CodeFirstCommonBaseAttribute>(false);
                return x.DeclaringType == documentClrType || attr != null;
            }).SelectMany(x =>
            {
                return x.PropertyType.GetProperties().Where(y =>
                {
                    return (y.DeclaringType == x.PropertyType || y.DeclaringType.GetCodeFirstAttribute<CodeFirstCommonBaseAttribute>(false) != null) && y.GetCodeFirstAttribute<ContentPropertyAttribute>() != null;
                }).Select(y =>
                    {
                      var attr = y.GetCodeFirstAttribute<ContentPropertyAttribute>();
                      var tabAttr = x.GetCodeFirstAttribute<ContentTabAttribute>();
                      return attr.AddTabAliasToPropertyAlias ? attr.Alias + "_" + tabAttr.Name.Replace(" ", "_") : attr.Alias;
                    });
            })).ToList();

            propertiesToKeep.AddRange(documentClrType.GetCustomAttributes<DoNotRemovePropertyAttribute>(true).Select(x => x.PropertyAlias));

            //loop through all the properties on the ContentType to see if they should be removed.
            var existingUmbracoProperties = contentType.PropertyTypes.ToArray();
            int length = contentType.PropertyTypes.Count();
            for (int i = 0; i < length; i++)
            {
                if (!propertiesToKeep.Any(x => x.Equals(existingUmbracoProperties[i].Alias, StringComparison.InvariantCultureIgnoreCase)))
                {
                    if (contentType.PropertyTypeExists(existingUmbracoProperties[i].Alias))
                    {
                        modified = true;
                        //remove the property
                        contentType.RemovePropertyType(existingUmbracoProperties[i].Alias);
                        var alias = existingUmbracoProperties[i].Alias;
                        var children = GetChildren(contentType);

                        //TODO is this needed? I reckon it shouldn't be
                        RemovePropertyFromChildren(alias, children);
                    }
                }
            }

            if (modified)
            {
                contentType.ResetDirtyProperties(false);
                Save(contentType);
            }
        }

        private void RemovePropertyFromChildren(string alias, IEnumerable<IContentTypeBase> children)
        {
            foreach (var child in children)
            {
                if (child.PropertyTypeExists(alias))
                {
                    child.RemovePropertyType(alias);
                    alias = child.Alias;
                    children = GetChildren(child);
                    RemovePropertyFromChildren(alias, children);
                }
            }
        }

        /// <summary>
        /// Scan the properties on tabs
        /// </summary>
        /// <param name="propertyTab"></param>
        /// <param name="contentType"></param>
        /// <param name="tabName"></param>
        /// <returns></returns>
        private IEnumerable<string> VerifyAllPropertiesOnTab(PropertyInfo propertyTab, IContentTypeBase contentType, TabRegistration registration, Type documentType, ref bool modified)
        {
            Type type = propertyTab.PropertyType;
            var properties = type.GetProperties().Where(x => x.GetCustomAttribute<ContentPropertyAttribute>() != null); //Do NOT initialise attribute here, causes initialisation exception
            var props = new List<PropertyRegistration>();
            registration._properties = props;
            registration.ClrType = type;

            if (properties.Count() > 0)
            {
                if (!contentType.PropertyGroups.Any(x => x.Name == registration.Name))
                {
                    contentType.AddPropertyGroup(registration.Name);
                }
                List<string> propertyAliases = new List<string>();
                foreach (var item in properties)
                {
                    CodeFirstManager.Current.Log("Syncing property " + item.Name + " on tab " + registration.Name + " of content type " + contentType.Name, this);
                    var propReg = _propertyModule.VerifyExistingProperty(contentType, registration, item, documentType, ref modified);
                    propertyAliases.Add(propReg.Alias);
                    props.Add(propReg);
                }
                return propertyAliases;
            }
            return new string[0];
        }
        #endregion

        #region Common
        /// <summary>
        /// Gets the allowed children
        /// </summary>
        /// <param name="types"></param>
        /// <returns></returns>
        private List<ContentTypeSort> FetchAllowedContentTypes(Type[] types, Type parentType)
        {
            if (types == null) return new List<ContentTypeSort>();

            List<ContentTypeSort> contentTypeSorts = new List<ContentTypeSort>();

            List<string> aliases = GetAliasesFromTypes(types, parentType);

            var contentTypes = AllContentTypes.Where(x => aliases.Contains(x.Alias)).ToArray();

            int length = contentTypes.Length;
            for (int i = 0; i < length; i++)
            {
                ContentTypeSort sort = new ContentTypeSort();
                sort.Alias = contentTypes[i].Alias;
                int id = contentTypes[i].Id;
                sort.Id = new Lazy<int>(() => { return id; });
                sort.SortOrder = i;
                contentTypeSorts.Add(sort);
            }
            return contentTypeSorts;
        }

        /// <summary>
        /// Gets all the document type aliases from the supplied list of types
        /// </summary>
        private List<string> GetAliasesFromTypes(Type[] types, Type parentType)
        {
            List<string> aliases = new List<string>();

            foreach (Type type in types)
            {
                var attribute = type.GetCodeFirstAttribute<ContentTypeAttribute>();
                if (attribute != null)
                {
					ContentTypeRegistration ct;
					if (!TryGetContentType(attribute.Alias, out ct))
					{
						throw new CodeFirstException(type.FullName + " is used as an allowed child of " + parentType.FullName + " but it is not registered with Code-First");
					}
                    aliases.Add(attribute.Alias);
                }
				else
				{
					throw new CodeFirstException(type.FullName + " is used as an allowed child of " + parentType.FullName + " but it does not have a [ContentType] attribute");
				}
            }

            return aliases;
        }
        #endregion

        #region IEntityTreeFilter
        public abstract bool IsFilter(string treeAlias);

        public virtual void Filter(Umbraco.Web.Models.Trees.TreeNodeCollection nodes, out bool changesMade)
        {
            var toRemove = new List<TreeNode>();

            foreach (var node in nodes)
            {
                var n = node.Alias;
                if (ContentTypeRegister.Registrations.Any(x => x.Name == node.Name))
                {
                    toRemove.Add(node);
                }
            }

            changesMade = toRemove.Count > 0;

            foreach (var node in toRemove)
            {
                nodes.Remove(node);
            }
        }
        #endregion

        #region IClassFileGenerator Helpers
        protected void GenerateContentTypes(string nameSpace, string folderPath, string attributeName, Func<IEnumerable<IContentTypeComposition>> selector, Action<IContentTypeComposition, ContentTypeDescription> customAction = null)
        {
            List<ContentTypeDescription> types;
            BuildContentTypeModels(nameSpace, folderPath, attributeName, out types, selector, customAction);
            GenerateClassFiles(nameSpace, folderPath, types);
        }

        private void GenerateClassFiles(string nameSpace, string folderPath, List<ContentTypeDescription> types)
        {
            foreach (var type in types)
            {
                UmbracoCodeFirstContentType dt = new UmbracoCodeFirstContentType();
                dt.Namespace = nameSpace;
                dt.Model = type;
                var output = dt.TransformText();
                System.IO.File.WriteAllText(System.IO.Path.Combine(folderPath, type.ClassName + ".cs"), output);
            }
        }

        private void BuildContentTypeModels(string nameSpace, string folderPath, string attributeName, out List<ContentTypeDescription> types, Func<IEnumerable<IContentTypeComposition>> selector, Action<IContentTypeComposition, ContentTypeDescription> customAction)
        {
            types = new List<ContentTypeDescription>();
            foreach (var node in selector.Invoke())
            {
                var type = BuildContentTypeModel(nameSpace, node, attributeName, customAction);
                types.Add(type);
            }
        }

        private ContentTypeDescription BuildContentTypeModel(string nameSpace, IContentTypeComposition node, string attributeName, Action<IContentTypeComposition, ContentTypeDescription> customAction)
        {
            var type = CreateTypeDescription(node, attributeName);
            if (customAction != null)
            {
                customAction.Invoke(node, type);
            }
            AddAllowedChildTypes(node, type);
            AddTabs(nameSpace, node, type);
            type.Properties = new List<PropertyDescription>();
            type.Compositions = new List<CompositionDescription>();
            AddProperties(type.Properties, node.PropertyTypes.Except(node.PropertyGroups.SelectMany(x => x.PropertyTypes)).Where(x => !type.IgnoredPropertyAliases.Any(y => y.Equals(x.Alias, StringComparison.InvariantCultureIgnoreCase))), nameSpace, type.ClassName);
            AddCompositions(type.Compositions, node.ContentTypeComposition, type.ParentAlias);
            return type;
        }

        private void AddCompositions(List<CompositionDescription> descriptions, IEnumerable<IContentTypeComposition> items, string parentAlias)
        {
            foreach (var item in items.Where(x => x.Alias != parentAlias))
            {
                var name = TypeGeneratorUtils.GetFormattedMemberName(item.Alias);
                descriptions.Add(new CompositionDescription() { TypeName = name, PropertyName = name + "Composition" });
            }
        }

        private void AddTabs(string nspace, IContentTypeComposition node, ContentTypeDescription type)
        {
            type.Tabs = new List<TabDescription>();
            foreach (var group in node.PropertyGroups.Where(x => !type.IgnoredTabs.Any(y => y.Equals(x.Name, StringComparison.InvariantCultureIgnoreCase))))
            {
                var tab = BuildTabModel(nspace, group, type.IgnoredPropertyAliases, type.ClassName);
                type.Tabs.Add(tab);
            }
        }

        private TabDescription BuildTabModel(string nameSpace, PropertyGroup group, List<string> ignoredProperties, string typeName)
        {
            var tab = new TabDescription();
            tab.TabName = group.Name;
            tab.TabClassName = TypeGeneratorUtils.GetFormattedMemberName(group.Name) + "Tab";
            tab.TabPropertyName = TypeGeneratorUtils.GetFormattedMemberName(group.Name);
            if (tab.TabPropertyName == typeName)
            {
                tab.TabPropertyName += "TabProperty";
            }
            tab.SortOrder = group.SortOrder.ToString();
            tab.Properties = new List<PropertyDescription>();
            AddProperties(tab.Properties, group.PropertyTypes.Where(x => !ignoredProperties.Any(y => y.Equals(x.Alias, StringComparison.InvariantCultureIgnoreCase))), nameSpace, typeName, "_" + TypeGeneratorUtils.GetFormattedMemberName(group.Name));
            return tab;
        }

        private void AddAllowedChildTypes(IContentTypeComposition node, ContentTypeDescription type)
        {
            if (node.AllowedContentTypes.Count() > 0)
            {
                var typeArray = "";
                foreach (var allowedType in node.AllowedContentTypes)
                {
                    if (typeArray != "")
                    {
                        typeArray += ", ";
                    }
                    typeArray += "typeof(" + TypeGeneratorUtils.GetFormattedMemberName(allowedType.Alias) + ")";
                }
                type.AllowedChildren = "new Type[] { " + typeArray + " }";
            }
            else
            {
                type.AllowedChildren = "null";
            }
        }

        private ContentTypeDescription CreateTypeDescription(IContentTypeComposition node, string attributeName)
        {
            var type = new ContentTypeDescription();
            type.AttributeName = attributeName;
            type.Alias = node.Alias;
            type.Name = node.Name;
            type.AllowAtRoot = node.AllowedAsRoot.ToString().ToLower();
            type.ClassName = TypeGeneratorUtils.GetFormattedMemberName(node.Alias); 
            type.EnableListView = node.IsContainer.ToString().ToLower();
            type.Icon = node.Icon;
            type.Description = node.Description == null ? "null" : node.Description.Replace(@"""", @"\""");
            return type;
        }

        private void AddProperties(List<PropertyDescription> list, IEnumerable<PropertyType> propertyTypeCollection, string nspace, string typeName, string tabName = null)
        {
            foreach (var propType in propertyTypeCollection)
            {
                PropertyDescription prop = BuildPropertyModel(nspace, tabName, propType, typeName);
                list.Add(prop);
            }
        }

        private PropertyDescription BuildPropertyModel(string nameSpace, string tabName, PropertyType propType, string typeName)
        {
            PropertyDescription prop = new PropertyDescription();
            prop.Alias = propType.Alias;
            if (tabName != null)
            {
                prop.Alias = prop.Alias.Replace(tabName, "");
            }
            prop.Name = propType.Name;
            prop.PropertyEditorAlias = propType.PropertyEditorAlias;
            prop.PropertyName = TypeGeneratorUtils.GetFormattedMemberName(propType.Alias.ToPascalCase());
            if (prop.PropertyName == typeName)
            {
                prop.PropertyName += "Property";
            }
            prop.DataTypeClassName = TypeGeneratorUtils.GetDataTypeClassName(propType.DataTypeDefinitionId, nameSpace);
            prop.DataTypeInstanceName = ApplicationContext.Current.Services.DataTypeService.GetDataTypeDefinitionById(propType.DataTypeDefinitionId).Name;
            prop.Mandatory = propType.Mandatory.ToString().ToLower();
            prop.Description = propType.Description == null ? "" : propType.Description.Replace("\"", "\"\"");
            prop.SortOrder = propType.SortOrder.ToString();
			prop.ValidationRegex = propType.ValidationRegExp;
            return prop;
        }
        #endregion
    }
}
