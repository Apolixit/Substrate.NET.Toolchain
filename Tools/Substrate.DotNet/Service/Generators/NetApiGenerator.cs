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
using Substrate.DotNet.Extensions;

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

      /// <summary>
      /// Generate classes from multi blocks versions
      /// </summary>
      /// <param name="blockVersions"></param>
      protected override void GenerateClasses(List<BlockVersion> blockVersions)
      {
         var resolvers = new List<NodeTypeResolver>();

         uint lastIndex = BuildVersionsResolvers(blockVersions, resolvers);

         // Add custom EnumType type
         //(NodeTypeResolver enumTypeResolver, _) = BuildStaticEnumType(lastIndex);
         //resolvers.Add(enumTypeResolver);

         var refinedNodes = resolvers
            .SelectMany(resolver => resolver.TypeNames.Select(nodePair => new NodeTypeRefinedChild(nodePair.Value, null, resolver)))
            .Cast<NodeTypeRefined>()
            .ToList();

         IEnumerable<(string name, IEnumerable<NodeTypeRefined> baseNode)> groupedNodes = GroupCommonNodeByVersion(blockVersions, refinedNodes);

         var unifiedResolver = new NodeTypeResolver(NodeRuntime, ProjectName, new Dictionary<uint, NodeType>(), null);

         IDictionary<uint, uint> mappingMother = GenerateMothersClasses(refinedNodes, groupedNodes, unifiedResolver);

         MapChildrenTypeToMotherType(refinedNodes, mappingMother);
         BuildConsolidateResolver(refinedNodes, unifiedResolver);
         GenerateTypesMultiVersion(refinedNodes, _projectSettings.ProjectDirectory, write: true);
      }

      private static IDictionary<uint, uint> GenerateMothersClasses(
         List<NodeTypeRefined> refinedNodes,
         IEnumerable<(string name, IEnumerable<NodeTypeRefined> baseNode)> groupedNodes,
         NodeTypeResolver unifiedResolver)
      {
         // This mapping is defined to redirect children Id to their mother Id
         IDictionary<uint, uint> mappingMother = new Dictionary<uint, uint>();
         foreach ((string name, IEnumerable<NodeTypeRefined> baseNode) in groupedNodes)
         {
            var childNodeTypeComposite = baseNode.First().NodeResolved.NodeType as NodeTypeComposite;

            var motherNodeTypeName = NodeTypeName.Generated(unifiedResolver, name + "Base");
            uint motherNodeId = refinedNodes.Last().Index + 1;
            string[] motherPath = buildMotherPath(childNodeTypeComposite);

            // Create an EnumType generic class for each EnumExt<>
            //NodeTypeField[] motherNodeTypeField = AddEnumTypeTo(enumTypeIndex, refinedNodes, childNodeTypeComposite, CreateEnumTypeMotherNode);
            
            // Finally create the new mother class
            var motherNodeTypeComposite = new NodeTypeComposite()
            {
               Id = motherNodeId,
               TypeFields = new List<NodeTypeField>(childNodeTypeComposite.TypeFields?.ToList() ?? new List<NodeTypeField>()).ToArray(),
               Docs = childNodeTypeComposite.Docs,
               Path = motherPath,
               TypeDef = childNodeTypeComposite.TypeDef,
               TypeParams = childNodeTypeComposite.TypeParams
            };

            // Add the new class to the mother classes mapping
            mappingMother.Add(childNodeTypeComposite.Id, motherNodeId);

            foreach (NodeTypeRefined otherGroupedNodes in baseNode.Skip(1))
            {
               var otherChildrenTypeComposite = (NodeTypeComposite)otherGroupedNodes.NodeResolved.NodeType;
               if (otherChildrenTypeComposite.TypeFields is null)
               {
                  Log.Debug($"Type composite {otherChildrenTypeComposite.Id} has TypeFields null");
               }
               else
               {
                  otherChildrenTypeComposite.TypeFields.ToList().ForEach(p =>
                  {
                     NodeTypeField existingField = motherNodeTypeComposite.TypeFields.FirstOrDefault(x => x.Name == p.Name);
                     if (existingField is null)
                     {
                        // If version changed, change mother type to BaseType
                        //motherNodeTypeComposite.TypeFields.ToList().Add(p);
                        AddPropToMotherTypeField(p, motherNodeTypeComposite);
                     } else if(existingField.Name is not null)
                     {
                        ManageNewPropertyVersion(refinedNodes, p, motherNodeTypeComposite, existingField);
                     }
                  });

                  mappingMother.Add(otherChildrenTypeComposite.Id, motherNodeId);

                  //otherChildrenTypeComposite.TypeFields = AddEnumTypeTo(enumTypeIndex, refinedNodes, otherChildrenTypeComposite, AdaptEnumTypeChildNode);
               }
            }

            // Add mother node to refined nodes list
            var motherNode = new NodeTypeResolved(motherNodeTypeComposite, motherNodeTypeName);
            var motherClass = new NodeTypeRefinedMother(motherNode, unifiedResolver);
            refinedNodes.Add(motherClass);

            foreach (NodeTypeRefinedChild childrenNode in baseNode)
            {
               //((NodeTypeComposite)childrenNode.baseNode.NodeResolved.NodeType).TypeFields = new List<NodeTypeField>().ToArray();
               childrenNode.LinkedTo = motherClass;
            }
         }

         return mappingMother;
      }

      private static void ManageNewPropertyVersion(List<NodeTypeRefined> refinedNodes, NodeTypeField p, NodeTypeComposite motherNodeTypeComposite, NodeTypeField existingField)
      {
         NodeType n1 = refinedNodes.First(x => x.Index == existingField.TypeId).NodeResolved.NodeType;
         NodeType n2 = refinedNodes.First(x => x.Index == p.TypeId).NodeResolved.NodeType;

         if (IsTypeChanged(n1, n2) || (n1.Path is not null && n2.Path is not null))
         {
            string newPropName = RenamePropertyWithNewVersion(p.Name);
            if (IsPathChanged(n1, n2) || IsTypeChanged(n1, n2))
            {
               p.Name = newPropName;

               if(!DoesExistInMotherProp(motherNodeTypeComposite, newPropName))
               {
                  AddPropToMotherTypeField(p, motherNodeTypeComposite);
               }
            }
         }

         static bool IsPathChanged(NodeType n1, NodeType n2)
         {
            if (n1.Path is not null && n2.Path is null) { return true; }
            if (n1.Path is null && n2.Path is not null) { return true; }

            if(n1.Path is not null && n2.Path is not null)
            {
               return string.Join(".", n1.Path) != string.Join(".", n2.Path);
            }

            return false;
            
         }

         static bool IsTypeChanged(NodeType n1, NodeType n2)
         {
            return n1.TypeDef != n2.TypeDef;
         }
      }

      private static string RenamePropertyWithNewVersion(string propName)
      {
         if (char.IsNumber(propName[^1]))
         {
            int num = propName[^1];
            return propName.Take(propName.Length - 2).ToString() + (num + 1);
         } else
         {
            return propName + "1";
         }
      }

      private static bool DoesExistInMotherProp(NodeTypeComposite motherNodeTypeComposite, string name)
      {
         return motherNodeTypeComposite.TypeFields.Any(x => x.Name == name);
      }

      private static void AddPropToMotherTypeField(NodeTypeField p, NodeTypeComposite motherNodeTypeComposite)
      {
         var extensionProp = motherNodeTypeComposite.TypeFields.ToList();
         extensionProp.Add(p);
         motherNodeTypeComposite.TypeFields = extensionProp.ToArray();
      }

      private static string[] buildMotherPath(NodeTypeComposite childNodeTypeComposite)
      {
         string[] motherPath = new List<string>(childNodeTypeComposite.Path).ToArray();
         motherPath[motherPath.Count() - 1] = motherPath.Last() + "Base";
         return motherPath;
      }

      //private static NodeTypeField[] AddEnumTypeTo(uint enumTypeIndex, List<NodeTypeRefined> refinedNodes, NodeTypeComposite childNodeTypeComposite, Action<uint, List<NodeTypeField>, List<NodeTypeField>> exec)
      //{
      //   var adaptedNodeTypeField = new List<NodeTypeField>(
      //      childNodeTypeComposite.TypeFields?.ToList() ?? new List<NodeTypeField>());

      //   var variantProperties = adaptedNodeTypeField
      //      .Where(x => refinedNodes.Single(y => y.Index == x.TypeId).NodeResolved.NodeType.TypeDef == TypeDefEnum.Variant)
      //      .ToList();

      //   if (variantProperties.Any())
      //   {
      //      //exec(enumTypeIndex, adaptedNodeTypeField, variantProperties);
      //   }

      //   return adaptedNodeTypeField.ToArray();
      //}

      //private static void CreateEnumTypeMotherNode(uint enumTypeIndex, List<NodeTypeField> motherNodeTypeField, List<NodeTypeField> variantProperties)
      //{
      //   variantProperties.ForEach(x => motherNodeTypeField.Remove(x));
      //   AdaptEnumTypeChildNode(enumTypeIndex, motherNodeTypeField, variantProperties);
      //   //var newProp = variantProperties.Select(x => new NodeTypeField()
      //   //{
      //   //   Docs = x.Docs,
      //   //   Name = x.Name + "Base",
      //   //   TypeId = enumTypeIndex,
      //   //   TypeName = x.TypeName,
      //   //}).ToList();

      //   //newProp.ForEach(x => motherNodeTypeField.Add(x));
      //}

      private static void AdaptEnumTypeChildNode(uint enumTypeIndex, List<NodeTypeField> nodeTypeField, List<NodeTypeField> variantProperties)
      {
         var newProp = variantProperties.Select(x => new NodeTypeField()
         {
            Docs = x.Docs,
            Name = x.Name + "Base",
            TypeId = enumTypeIndex,
            TypeName = x.TypeName,
         }).ToList();

         newProp.ForEach(x => nodeTypeField.Add(x));
      }


      private static void BuildConsolidateResolver(List<NodeTypeRefined> refinedNodes, NodeTypeResolver unifiedResolver)
      {
         var unifiedDictionnary = new Dictionary<uint, NodeTypeResolved>();
         var motherNodes = refinedNodes.ToList();
         foreach (NodeTypeRefined md in motherNodes)
         {
            unifiedDictionnary.Add(md.Index, md.NodeResolved);
         }
         unifiedResolver.SetTypeNames(unifiedDictionnary);

         refinedNodes.ToList().ForEach(x =>
         {
            x.Resolver = unifiedResolver;
         });
      }

      private static void MapChildrenTypeToMotherType(List<NodeTypeRefined> refinedNodes, IDictionary<uint, uint> mappingMother)
      {
         bool hasSomethingChanged = false;
         do
         {
            var mn = refinedNodes.Where(x => x.LevelNode == NodeTypeRefined.LevelTypeNode.Mother).ToDictionary(x => x.Index, x => x.NodeResolved.NodeType);
            hasSomethingChanged = MapSourceToDestination(mn, mappingMother);
         } while (hasSomethingChanged);
      }

      private static IEnumerable<(string, IEnumerable<NodeTypeRefined>)> GroupCommonNodeByVersion(List<BlockVersion> blockVersions, List<NodeTypeRefined> refinedNodes)
      {
         var versionsReplace = blockVersions.Select(y => $"{y.SpecVersion}").ToList();

         //IEnumerable<IGrouping<string, (NodeTypeRefined baseNode, string baseName)>> groupedNodes = refinedNodes
         IEnumerable<(string Key, IEnumerable<NodeTypeRefined>)> groupedNodes = refinedNodes
            .Where(x => x.NodeResolved.NodeType is NodeTypeComposite)
            .Select(baseNode =>
            {
               string nodeNamespace = baseNode.NodeResolved.Name.BaseName.ToString();
               versionsReplace.ForEach(y => nodeNamespace = nodeNamespace.Replace(y, "base"));
               return (baseNode, baseName: nodeNamespace);
            }).GroupBy(x => x.baseName)
            .Select(x => (x.Key, x.Select(y => y.baseNode)));

         return groupedNodes;
      }

      /// <summary>
      /// Generate a "fake" type to handle EnumExt<> in mother classes
      /// </summary>
      /// <param name="lastIndex"></param>
      /// <returns></returns>
      private (NodeTypeResolver, uint) BuildStaticEnumType(uint lastIndex)
      {
         uint enumTypeIndex = lastIndex + 1;
         var enumType = new NodeTypeComposite()
         {
            Id = enumTypeIndex,
            Path = new List<string>() { "x", "y" }.ToArray(),
            TypeDef = TypeDefEnum.Composite
         };

         return (
            new NodeTypeResolver(NodeRuntime, ProjectName, new Dictionary<uint, NodeType>
            {
               { enumTypeIndex, enumType }
            }, null),
            enumTypeIndex);
      }

      /// <summary>
      /// Generate list of resolver with unique index for each node type
      /// </summary>
      /// <param name="blockVersions"></param>
      /// <param name="resolvers"></param>
      /// <returns></returns>
      private uint BuildVersionsResolvers(List<BlockVersion> blockVersions, List<NodeTypeResolver> resolvers)
      {
         uint lastIndex = 0;
         foreach (BlockVersion blockVersion in blockVersions)
         {
            SolutionGeneratorBase.RefineVecWrapper(blockVersion.Metadata);

            // For every block version (except the first one), index are shifted to be unique
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
         }

         return lastIndex;
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