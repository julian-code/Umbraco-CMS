// Copyright (c) Umbraco.
// See LICENSE for more details.

using System.Collections.Generic;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Linq;
using Umbraco.Extensions;
using Umbraco.Cms.Infrastructure.PublishedCache;
using Umbraco.Cms.Tests.Common.Builders;
using System;
using Umbraco.Cms.Infrastructure.PublishedCache.DataSource;
using Umbraco.Cms.Tests.Common.Builders.Extensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.PropertyEditors;
using Moq;
using Umbraco.Cms.Infrastructure.Serialization;

namespace Umbraco.Cms.Tests.Common.Published
{
    /// <summary>
    /// Converts legacy Umbraco XML structures to NuCache <see cref="ContentNodeKit"/> collections
    /// to populate a test implementation of <see cref="INuCacheContentService"/>
    /// </summary>
    public static class PublishedContentXmlAdapter
    {
        public static IEnumerable<ContentNodeKit> GetContentNodeKits(
            string xml,
            IShortStringHelper shortStringHelper,
            out ContentType[] contentTypes,
            out DataType[] dataTypes)
        {
            // use the label data type for all data for these tests except in the case
            // where a property is named 'content', in which case use the RTE. 
            var serializer = new ConfigurationEditorJsonSerializer();
            var labelDataType = new DataType(new VoidEditor("Label", Mock.Of<IDataValueEditorFactory>()), serializer) { Id = 3 };
            var rteDataType = new DataType(new VoidEditor("RTE", Mock.Of<IDataValueEditorFactory>()), serializer) { Id = 4 };
            dataTypes = new[] { labelDataType, rteDataType };

            var kitsAndXml = new List<(ContentNodeKit kit, XElement node)>();

            var xDoc = XDocument.Parse(xml);
            IEnumerable<XElement> nodes = xDoc.XPathSelectElements("//*[@isDoc]");
            foreach (XElement node in nodes)
            {
                var id = node.AttributeValue<int>("id");
                Guid key = node.AttributeValue<Guid?>("key") ?? id.ToGuid();

                var propertyElements = node.Elements().Where(x => x.Attribute("id") == null);
                var properties = new Dictionary<string, PropertyData[]>();
                foreach(XElement propertyElement in propertyElements)
                {
                    properties[propertyElement.Name.LocalName] = new[]
                    {
                        // TODO: builder? 
                        new PropertyData
                        {
                            Culture = string.Empty,
                            Segment = string.Empty,
                            Value = propertyElement.Value
                        }
                    };
                }

                var contentData = new ContentDataBuilder()
                    .WithName(node.AttributeValue<string>("nodeName"))
                    .WithProperties(properties)
                    .WithPublished(true)
                    .WithTemplateId(node.AttributeValue<int>("template"))
                    .WithUrlSegment(node.AttributeValue<string>("urlName"))
                    .WithVersionDate(node.AttributeValue<DateTime>("updateDate"))
                    .WithWriterId(node.AttributeValue<int>("writerID"))
                    .Build();

                ContentNodeKit kit = ContentNodeKitBuilder.CreateWithContent(
                    node.AttributeValue<int>("nodeType"),
                    id,
                    node.AttributeValue<string>("path"),
                    node.AttributeValue<int>("sortOrder"),
                    node.AttributeValue<int>("level"),
                    node.AttributeValue<int>("parentID"),
                    node.AttributeValue<int>("creatorID"),
                    key,
                    node.AttributeValue<DateTime>("createDate"),
                    contentData,
                    contentData);

                kitsAndXml.Add((kit, node));
            }

            // put together the unique content types
            var contentTypesIdToType = new Dictionary<int, ContentType>();
            foreach((ContentNodeKit kit, XElement node) in kitsAndXml)
            {
                if (!contentTypesIdToType.TryGetValue(kit.ContentTypeId, out ContentType contentType))
                {
                    contentType = new ContentType(shortStringHelper, -1)
                    {
                        Id = kit.ContentTypeId,
                        Alias = node.Name.LocalName
                    };
                    SetContentTypeProperties(shortStringHelper, labelDataType, rteDataType, kit, contentType);
                    contentTypesIdToType[kit.ContentTypeId] = contentType;
                }
                else
                {
                    // we've already created it but might need to add properties
                    SetContentTypeProperties(shortStringHelper, labelDataType, rteDataType, kit, contentType);
                } 
            }

            contentTypes = contentTypesIdToType.Values.ToArray();

            return kitsAndXml.Select(x => x.kit);
        }

        private static void SetContentTypeProperties(IShortStringHelper shortStringHelper, DataType labelDataType, DataType rteDataType, ContentNodeKit kit, ContentType contentType)
        {
            foreach (KeyValuePair<string, PropertyData[]> property in kit.DraftData.Properties)
            {
                var propertyType = new PropertyType(shortStringHelper, labelDataType, property.Key);

                if (!contentType.PropertyTypeExists(propertyType.Alias))
                {
                    if (propertyType.Alias == "content")
                    {
                        propertyType.DataTypeId = rteDataType.Id;
                    }
                    contentType.AddPropertyType(propertyType);
                }
            }
        }
    }
}
