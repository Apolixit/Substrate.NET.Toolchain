using Substrate.DotNet.Service.Generators.Base;
using Substrate.DotNet.Service.Node;
using Substrate.NetApi.Model.Meta;
using Serilog;
using System.Collections.Generic;
using Substrate.DotNet.Client.Versions;
using System;
using Microsoft.CodeAnalysis;
using System.Linq;
using System.CodeDom;
using Substrate.ServiceLayer.Extensions;
using Substrate.NetApi.Model.Types.Metadata.V14;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Substrate.DotNet.Service.Generators
{
   /// <summary>
   /// Responsible for generating the NetApi Solution
   /// </summary>
   public class NetApiGenerator : SolutionGeneratorBase
   {
      private readonly ProjectSettings _projectSettings;

      public NetApiGenerator(ILogger logger, string nodeRuntime, ProjectSettings projectSettings) : base(logger, nodeRuntime, projectSettings.ProjectName)
      {
         _projectSettings = projectSettings;
      }

      protected override void GenerateClasses(List<BlockVersion> blockVersions)
      {
         //Dictionary<uint, NodeType> refinedTypes = new();
         var resolvers = new List<NodeTypeResolver>();

         
         uint lastIndex = 0;
         foreach (BlockVersion blockVersion in blockVersions)
         {
            //int index = blockVersions.IndexOf(blockVersion) + 1;
            //SolutionGeneratorBase.SwitchNodeIds(blockVersion.Metadata);

            //IDictionary<uint, uint> newIds = blockVersion.Metadata.NodeMetadata.Types.Values.Select(t => (t.Id, t.Id + nbElem)).ToDictionary(x => x.Id, x => x.Item2);

            if (lastIndex > 0)
            {
               uint nbToAdd = Math.Max((uint)blockVersion.Metadata.NodeMetadata.Types.Values.Count, lastIndex + 1);
               IDictionary<uint, uint> mapping = blockVersion.Metadata.NodeMetadata.Types.Values.Select(t => (t.Id, t.Id + nbToAdd)).ToDictionary(x => x.Id, x => x.Item2);
               
               SolutionGeneratorBase.ShiftNodeIds(blockVersion.Metadata, mapping);
               SolutionGeneratorBase.SwitchNodeIds(blockVersion.Metadata, mapping, removeOldIds: false);
            }

            GetGenericStructs(blockVersion.Metadata.NodeMetadata.Types);

            var resolver = new NodeTypeResolver(NodeRuntime, ProjectName, blockVersion.Metadata.NodeMetadata.Types, blockVersion);
            resolvers.Add(resolver);

            lastIndex = resolver.TypeNames.Last().Value.NodeType.Id;
            //nbElem += (uint)blockVersion.Metadata.NodeMetadata.Types.Count;
            //isFirst = false;
         }

         var refinedNodes = resolvers
            .SelectMany(resolver => resolver.TypeNames.Select(nodePair => new NodeTypeRefinedChild(nodePair.Value, null, resolver)))
            .Cast<NodeTypeRefined>()
            .ToList();

         //IEnumerable<IGrouping<uint, NodeTypeRefined>> bugs = refinedNodes.GroupBy(x => x.Index).Where(x => x.Count() > 1);
         // Ugly sorry ...
         var versionsReplace = blockVersions.Select(y => $"{y.SpecVersion}").ToList();

         IEnumerable<IGrouping<string, (NodeTypeRefined baseNode, string baseName)>> groupedNodes = refinedNodes
            .Where(x => x.NodeResolved.NodeType is NodeTypeComposite)
            .Select(baseNode =>
            {
               string nodeNamespace = baseNode.NodeResolved.Name.BaseName.ToString();
               versionsReplace.ForEach(y => nodeNamespace = nodeNamespace.Replace(y, "base"));
               return (baseNode, baseName: nodeNamespace);
            }).GroupBy(x => x.baseName);



         var motherResolver = new NodeTypeResolver(NodeRuntime, ProjectName, new Dictionary<uint, NodeType>(), null);
         IDictionary<uint, uint> mappingMother = new Dictionary<uint, uint>();

         foreach (IGrouping<string, (NodeTypeRefined baseNode, string nodeNamespace)> nodes in groupedNodes)
         {
            var motherNodeTypeName = NodeTypeName.Generated(motherResolver, nodes.First().nodeNamespace + "Base");
            uint motherNodeId = refinedNodes.Last().Index + 1;

            // Ajouter les propriétés des classes groupées !
            var childNodeTypeComposite = nodes.First().baseNode.NodeResolved.NodeType as NodeTypeComposite;

            string[] motherPath = new List<string>(childNodeTypeComposite.Path).ToArray();
            motherPath[motherPath.Count() - 1] = motherPath.Last() + "Base";

            var motherNodeTypeComposite = new NodeTypeComposite()
            {
               Id = motherNodeId,
               TypeFields = childNodeTypeComposite.TypeFields ?? new List<NodeTypeField>().ToArray(),
               Docs = childNodeTypeComposite.Docs,
               Path = motherPath,
               TypeDef = childNodeTypeComposite.TypeDef,
               TypeParams = childNodeTypeComposite.TypeParams
            };

            mappingMother.Add(childNodeTypeComposite.Id, motherNodeId);

            foreach ((NodeTypeRefined baseNode, string nodeNamespace) otherGroupedNodes in nodes.Skip(1))
            {
               var otherChildrenTypeComposite = (NodeTypeComposite)otherGroupedNodes.baseNode.NodeResolved.NodeType;
               if (otherChildrenTypeComposite.TypeFields is null)
               {
                  Log.Warning($"WTF, type composite {otherChildrenTypeComposite.Id} has TypeFields null");
               }
               else
               {
                  otherChildrenTypeComposite.TypeFields.ToList().ForEach(p =>
                  {
                     if (!motherNodeTypeComposite.TypeFields.Any(x => x.Name == p.Name))
                     {
                        motherNodeTypeComposite.TypeFields.ToList().Add(p);
                     }
                  });

                  mappingMother.Add(otherChildrenTypeComposite.Id, motherNodeId);
               }
            }

            var motherNode = new NodeTypeResolved(motherNodeTypeComposite, motherNodeTypeName);
            var motherClass = new NodeTypeRefinedMother(motherNode, motherResolver);
            refinedNodes.Add(motherClass);

            foreach ((NodeTypeRefinedChild baseNode, string nodeNamespace) childrenNode in nodes)
            {
               //((NodeTypeComposite)childrenNode.baseNode.NodeResolved.NodeType).TypeFields = new List<NodeTypeField>().ToArray();
               childrenNode.baseNode.LinkedTo = motherClass;
            }
         }

         bool hasSomethingChanged = false;
         do
         {
            var mn = refinedNodes.Where(x => x.LevelNode == NodeTypeRefined.LevelTypeNode.Mother).ToDictionary(x => x.Index, x => x.NodeResolved.NodeType);
            hasSomethingChanged = MapSourceToDestination(mn, mappingMother);
         } while (hasSomethingChanged);

         //SolutionGeneratorBase.SwitchNodeIds(refinedNodes.ToDictionary(x => x.Index, x => x.NodeResolved.NodeType), mappingMother, removeOldIds: false);

         var motherDictionnary = new Dictionary<uint, NodeTypeResolved>();
         var motherNodes = refinedNodes.ToList();
         //var motherNodes = refinedNodes.Where(x => x.LevelNode == NodeTypeRefined.LevelTypeNode.Mother).ToList();
         foreach (NodeTypeRefined md in motherNodes)
         {
            motherDictionnary.Add(md.Index, md.NodeResolved);
         }
         motherResolver.SetTypeNames(motherDictionnary);

         //refinedNodes.Where(x => x.LevelNode == NodeTypeRefined.LevelTypeNode.Mother).ToList().ForEach(x => {
         //   x.Resolver = motherResolver;
         //});

         refinedNodes.ToList().ForEach(x => {
            x.Resolver = motherResolver;
         });

         // Now for each type, let's create a class
         //var resTest = groupedNodes.Where(x => x.Count() < 3).ToList();
         //var res2 = groupedNodes.Where(x => x.Count() > 3).ToList();


         GenerateTypesMultiVersion(refinedNodes, _projectSettings.ProjectDirectory, write: true);
      }

      protected override void GenerateClasses(MetaData metadata, BlockVersion? blockVersion = null)
      {
         // dirty workaround for generics.
         // TODO (svnscha) Why dirty workaround?
         GetGenericStructs(metadata.NodeMetadata.Types);

         // generate types
         NodeTypeResolver typeDict = GenerateTypes(metadata.NodeMetadata.Types, _projectSettings.ProjectDirectory, write: true, blockVersion: blockVersion);

         // generate modules
         GenerateModules(ProjectName, metadata.NodeMetadata.Modules, typeDict, metadata.NodeMetadata.Types, _projectSettings.ProjectDirectory);

         // generate base event handler
         // TODO (svnscha) Why disabled?
         // GenerateBaseEvents(metadata.NodeMetadata.Modules, typeDict, metadata.NodeMetadata.Types);
      }

      private static void GenerateModules(string projectName, Dictionary<uint, PalletModule> modules, NodeTypeResolver typeDict, Dictionary<uint, NodeType> nodeTypes, string basePath)
      {
         List<string> modulesResolved = new();
         foreach (PalletModule module in modules.Values)
         {
            ModuleGenBuilder
                .Init(projectName, module.Index, module, typeDict, nodeTypes)
                .Create()
                .Build(write: true, out bool _, basePath);

            modulesResolved.Add($"{module.Name}Storage");
         }

         ClientBuilder
             .Init(projectName, 0, modulesResolved, typeDict).Create()
             .Build(write: true, out bool _, basePath);
      }
   }
}