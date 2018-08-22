﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Microsoft.Xna.Framework.Content.Pipeline;
using MonoGame.Extended.Tiled.Serialization;

namespace MonoGame.Extended.Content.Pipeline.Tiled
{
    public interface IExternalReferenceRepository
    {
        ExternalReference<TInput> GetExternalReference<TInput>(string source);
    }

    public class ContentItem<T> : ContentItem, IExternalReferenceRepository
    {
        public ContentItem(T data)
        {
            Data = data;
        }

        public T Data { get; }

        private readonly Dictionary<string, ContentItem> _externalReferences = new Dictionary<string, ContentItem>();

        public void BuildExternalReference<TInput>(ContentProcessorContext context, string source, OpaqueDataDictionary parameters = null)
        {
            var sourceAsset = new ExternalReference<TInput>(source);
            var externalReference = context.BuildAsset<TInput, TInput>(sourceAsset, "", parameters, "", "");
            _externalReferences.Add(source, externalReference);
        }

        public ExternalReference<TInput> GetExternalReference<TInput>(string source)
        {
            if (_externalReferences.TryGetValue(source, out var contentItem))
                return contentItem as ExternalReference<TInput>;

            return null;
        }

    }

    public class TiledMapContentItem : ContentItem<TiledMapContent>
    {
        public TiledMapContentItem(TiledMapContent data) 
            : base(data)
        {
        }
    }


    [ContentImporter(".tmx", DefaultProcessor = "TiledMapProcessor", DisplayName = "Tiled Map Importer - MonoGame.Extended")]
    public class TiledMapImporter : ContentImporter<TiledMapContentItem>
    {
        public override TiledMapContentItem Import(string filePath, ContentImporterContext context)
        {
            try
            {
                if (filePath == null)
                    throw new ArgumentNullException(nameof(filePath));

                ContentLogger.Logger = context.Logger;
                ContentLogger.Log($"Importing '{filePath}'");

                var map = DeserializeTiledMapContent(filePath, context);

                if (map.Width > ushort.MaxValue || map.Height > ushort.MaxValue)
                    throw new InvalidContentException($"The map '{filePath} is much too large. The maximum supported width and height for a Tiled map is {ushort.MaxValue}.");

                ContentLogger.Log($"Imported '{filePath}'");

                return new TiledMapContentItem(map);

            }
            catch (Exception e)
            {
                context.Logger.LogImportantMessage(e.StackTrace);
				throw;
            }
        }

        private static TiledMapContent DeserializeTiledMapContent(string mapFilePath, ContentImporterContext context)
        {
            using (var reader = new StreamReader(mapFilePath))
			{
				var mapSerializer = new XmlSerializer(typeof(TiledMapContent));
				var map = (TiledMapContent)mapSerializer.Deserialize(reader);

				map.FilePath = mapFilePath;

				for (var i = 0; i < map.Tilesets.Count; i++)
				{
					var tileset = map.Tilesets[i];

					if (!string.IsNullOrWhiteSpace(tileset.Source))
					{
						tileset.Source = $"{Path.GetDirectoryName(mapFilePath)}/{tileset.Source}";
						ContentLogger.Log($"Adding dependency for {tileset.Source}");
						// We depend on the tileset. If the tileset changes, the map also needs to rebuild.
						context.AddDependency(tileset.Source);
					}
					else
					{
						tileset.Image.Source = $"{Path.GetDirectoryName(mapFilePath)}/{tileset.Image.Source}";
						ContentLogger.Log($"Adding dependency for {tileset.Image.Source}");
						context.AddDependency(tileset.Image.Source);
					}
				}

				ImportLayers(context, map.Layers, Path.GetDirectoryName(mapFilePath));

				map.Name = mapFilePath;
				return map;
			}
		}

		private static void ImportLayers(ContentImporterContext context, List<TiledMapLayerContent> layers, string path)
		{
			for (var i = 0; i < layers.Count; i++)
			{
				if (layers[i] is TiledMapImageLayerContent imageLayer)
				{
					imageLayer.Image.Source = $"{path}/{imageLayer.Image.Source}";
					ContentLogger.Log($"Adding dependency for '{imageLayer.Image.Source}'");

					// Tell the pipeline that we depend on this image and need to rebuild the map if the image changes.
					// (Maybe the image is a different size)
					context.AddDependency(imageLayer.Image.Source);
				}
				if (layers[i] is TiledMapObjectLayerContent objectLayer)
					foreach (var obj in objectLayer.Objects)
						if (!String.IsNullOrWhiteSpace(obj.TemplateSource))
						{
							obj.TemplateSource = $"{path}/{obj.TemplateSource}";
							ContentLogger.Log($"Adding dependency for '{obj.TemplateSource}'");
							// Tell the pipeline that we depend on this template and need to rebuild the map if the template changes.
							// (Templates are loaded into objects on process, so all objects which depend on the template file
							//  need the change to the template)
							context.AddDependency(obj.TemplateSource);
						}
				if (layers[i] is TiledMapGroupLayerContent groupLayer)
					// Yay recursion!
					ImportLayers(context, groupLayer.Layers, path);
			}
		}
    }
}