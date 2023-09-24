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
using System.Reflection;
using static Substrate.DotNet.Service.Node.ModuleGenBuilder;

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

         var refinedNodes = resolvers
            .SelectMany(resolver => resolver.TypeNames.Select(nodePair => new NodeTypeRefinedChild(nodePair.Value, null, resolver)))
            .Cast<NodeTypeRefined>()
            .ToList();

         GenerateVersionnedModules(blockVersions, refinedNodes);

         IEnumerable<(string name, IEnumerable<NodeTypeRefined> baseNode)> groupedNodes = GroupCommonNodeByVersion(blockVersions, refinedNodes);

         var unifiedResolver = new NodeTypeResolver(NodeRuntime, ProjectName, new Dictionary<uint, NodeType>(), null);
         IDictionary<uint, uint> mappingMother = GenerateMothersClasses(refinedNodes, groupedNodes, unifiedResolver);
         Dictionary<uint, ModuleAggregation> motherModules = GenerateMothersModule(refinedNodes, blockVersions);

         MapChildrenTypeToMotherType(refinedNodes, motherModules, mappingMother);
         BuildConsolidateResolver(refinedNodes, unifiedResolver);

         GenerateModulesMultiVersion(motherModules, refinedNodes, unifiedResolver, mappingMother);
         GenerateTypesMultiVersion(refinedNodes, _projectSettings.ProjectDirectory, write: true);
      }

      private void GenerateVersionnedModules(List<BlockVersion> blockVersions, List<NodeTypeRefined> refinedNodes)
      {
         foreach (BlockVersion blockVersion in blockVersions)
         {
            NodeTypeResolver resolverVersion = refinedNodes.First(x => x.Resolver.ProjectSpecVersion == "v" + blockVersion.SpecVersion).Resolver;
            GenerateModules(ProjectName, blockVersion.Metadata.NodeMetadata.Modules, resolverVersion, refinedNodes.ToNodeDictionnary(), _projectSettings.ProjectDirectory);
         }
      }

      private Dictionary<uint, ModuleAggregation> GenerateMothersModule(List<NodeTypeRefined> refinedNodes, List<BlockVersion> blockVersions)
      {
         var modules = new Dictionary<uint, ModuleAggregation>();
         uint index = 0;
         //IEnumerable<IGrouping<string, KeyValuePair<uint, PalletModule>>> groupedModules = blockVersions.SelectMany(x => x.Metadata.NodeMetadata.Modules).GroupBy(x => x.Value.Name);

         var versionnedModule = new List<(uint version, PalletModule module)>();
         foreach (BlockVersion blockVersion in blockVersions)
         {
            foreach (KeyValuePair<uint, PalletModule> mod in blockVersion.Metadata.NodeMetadata.Modules)
            {
               versionnedModule.Add((blockVersion.SpecVersion, mod.Value));
            }
         }

         IEnumerable<IGrouping<string, (uint version, PalletModule module)>> groupedModules = versionnedModule.GroupBy(x => x.module.Name);

         foreach (IGrouping<string, (uint version, PalletModule module)> groupedModule in groupedModules)
         {
            //uint version;
            //uint.TryParse(groupedModule.Key.Skip(1).ToString(), out version);

            //groupedModule.All(x => refinedNodes.First(y => y.Index == x.Value.Errors.TypeId).pa)
            foreach ((uint currentVersion, PalletModule currentModule) in groupedModule)
            {

               KeyValuePair<uint, ModuleAggregation> existingModule = modules.SingleOrDefault(x => x.Value.AggregateModule.Name == groupedModule.Key);
               if (existingModule.Value is null)
               {
                  modules.Add(index, new ModuleAggregation()
                  {
                     AggregateModule = new PalletModule()
                     {
                        Index = currentModule.Index,
                        Name = currentModule.Name,
                        Calls = currentModule.Calls is null ? null : new PalletCalls()
                        {
                           TypeId = currentModule.Calls.TypeId
                        },
                        Errors = currentModule.Errors is null ? null : new PalletErrors()
                        {
                           TypeId = currentModule.Errors.TypeId
                        },
                        Events = currentModule.Events is null ? null : new PalletEvents()
                        {
                           TypeId = currentModule.Events.TypeId
                        },
                        Constants = currentModule.Constants is null ? null : currentModule.Constants.Select(x => new PalletConstant() { 
                           Docs = x.Docs, 
                           Name = x.Name, 
                           TypeId = x.TypeId, 
                           Value = x.Value}).ToArray(),
                        Storage = currentModule.Storage is null ? null : new PalletStorage()
                        { Prefix = currentModule.Storage.Prefix, Entries = currentModule.Storage.Entries.Select(x => new Entry()
                        {
                           Name = x.Name,
                           Default = x.Default,
                           Docs = x.Docs,
                           Modifier = x.Modifier,
                           StorageType = x.StorageType,
                           TypeMap = new(x.TypeMap.Item1, x.TypeMap.Item2 is null ? null : new TypeMap() { Key = x.TypeMap.Item2.Key, Value = x.TypeMap.Item2.Value, Hashers = x.TypeMap.Item2.Hashers})
                        }).ToArray() }
                     },
                     ModuleVersions = new List<ModuleVersion>() { new ModuleVersion() { Version = currentVersion, Module = currentModule } }
                  });
               }
               else
               {
                  AffectErrors(refinedNodes, currentModule, existingModule.Value.AggregateModule);
                  AffectConstants(currentModule, existingModule.Value.AggregateModule);
                  AffectCalls(refinedNodes, currentModule, existingModule.Value.AggregateModule);
                  AffectStorage(currentModule, existingModule.Value.AggregateModule);

                  existingModule.Value.ModuleVersions.Add(new ModuleVersion() { Version = currentVersion, Module = currentModule });
               }
            }
            index++;
         }

         return modules;

         static void AffectErrors(List<NodeTypeRefined> refinedNodes, PalletModule currentModule, PalletModule existingModule)
         {
            if (existingModule.Errors is null && currentModule.Errors is null)
            {
               return;
            }

            if (existingModule.Errors is null && currentModule.Errors is not null)
            {
               existingModule.Errors = currentModule.Errors;
               return;
            }

            NodeTypeVariant existing = existingModule.Errors is not null ? (NodeTypeVariant)refinedNodes.First(x => x.Index == existingModule.Errors.TypeId).NodeResolved.NodeType : null;
            NodeTypeVariant current = currentModule.Errors is not null ? (NodeTypeVariant)refinedNodes.First(x => x.Index == currentModule.Errors.TypeId).NodeResolved.NodeType : null;

            if (existing?.Variants is not null && current?.Variants is not null)
            {
               existing.Variants = existing.Variants.AddIfNotExists(current.Variants, (x, y) => x.Any(e => e.Name == y.Name)).ToArray();
            }
         }

         static void AffectConstants(PalletModule currentModule, PalletModule existingModule)
         {
            PalletConstant[] current = currentModule.Constants;

            // Error are variant (enum)
            existingModule.Constants = existingModule.Constants.AddIfNotExists(current, (x, y) => x.Any(e => e.Name == y.Name)).ToArray();
         }

         static void AffectCalls(List<NodeTypeRefined> refinedNodes, PalletModule currentModule, PalletModule existingModule)
         {
            if (existingModule.Calls is null && currentModule.Calls is null)
            {
               return;
            }

            if (existingModule.Calls is null && currentModule.Calls is not null)
            {
               existingModule.Calls = currentModule.Calls;
               return;
            }

            NodeTypeVariant existing = existingModule.Calls is not null ?(NodeTypeVariant)refinedNodes.First(x => x.Index == existingModule.Calls.TypeId).NodeResolved.NodeType : null;
            NodeTypeVariant current = currentModule.Calls is not null ? (NodeTypeVariant)refinedNodes.First(x => x.Index == currentModule.Calls.TypeId).NodeResolved.NodeType : null;

            if (existing?.Variants is not null && current?.Variants is not null)
            {
               existing.Variants = existing.Variants.AddIfNotExists(current.Variants, (x, y) => x.Any(e => e.Name == y.Name)).ToArray();
            }
         }

         static void AffectStorage(PalletModule currentModule, PalletModule existingModule)
         {

            if (existingModule.Storage is null && currentModule.Storage is null)
            {
               return;
            }

            if (existingModule.Storage is null && currentModule.Storage is not null)
            {
               existingModule.Storage.Entries = currentModule.Storage.Entries;
               return;
            }

            Entry[] current = currentModule.Storage.Entries;
            Entry[] currentCopy = currentModule.Storage.Entries.Select(x => new Entry()
            {
               Name = x.Name,
               Default = x.Default,
               Docs = x.Docs,
               Modifier = x.Modifier,
               StorageType = x.StorageType,
               TypeMap = new(x.TypeMap.Item1, x.TypeMap.Item2 is null ? null : new TypeMap() { Key = x.TypeMap.Item2.Key, Value = x.TypeMap.Item2.Value, Hashers = x.TypeMap.Item2.Hashers })
            }).ToArray();

            existingModule.Storage.Entries = existingModule.Storage.Entries.AddIfNotExists(currentCopy, (x, y) => x.Any(e => e.Name == y.Name)).ToArray();
         }

      }

      private void GenerateModulesMultiVersion(Dictionary<uint, ModuleAggregation> modules, List<NodeTypeRefined> refinedNodes, NodeTypeResolver unifiedResolver, IDictionary<uint, uint> mappingMother)
      {
         GenerateModules(ProjectName, modules, unifiedResolver, refinedNodes.ToNodeDictionnary(), _projectSettings.ProjectDirectory, TypeModule.Aggregation, mappingMother);

         //foreach (BlockVersion blockVersion in blockVersions)
         //{
         //   GenerateModules(ProjectName, blockVersion.Metadata.NodeMetadata.Modules, unifiedResolver, refinedNodes.ToNodeDictionnary(), _projectSettings.ProjectDirectory);


         //}
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

            // Finally create the new mother class
            var motherNodeTypeComposite = new NodeTypeComposite()
            {
               Id = motherNodeId,
               TypeFields = childNodeTypeComposite.TypeFields.Copy().ToArray(),
               Docs = childNodeTypeComposite.Docs,
               Path = motherPath,
               TypeDef = childNodeTypeComposite.TypeDef,
               TypeParams = childNodeTypeComposite.TypeParams.Copy().ToArray()
            };

            // Add the new class to the mother classes mapping
            mappingMother.Add(childNodeTypeComposite.Id, motherNodeId);

            foreach (NodeTypeRefined otherGroupedNodes in baseNode.Skip(1))
            {
               var otherChildrenTypeComposite = (NodeTypeComposite)otherGroupedNodes.NodeResolved.NodeType;
               if (otherChildrenTypeComposite.TypeFields is not null)
               {
                  otherChildrenTypeComposite.TypeFields.ToList().ForEach(p =>
                  {
                     NodeTypeField existingField = TryFindExistingField(p, motherNodeTypeComposite);

                     if (existingField is null)
                     {
                        // If version changed, change mother type to BaseType
                        AddPropToMotherTypeField(p, motherNodeTypeComposite);
                     }
                     else if (existingField.Name is not null)
                     {
                        ManageNewPropertyVersion(refinedNodes, motherNodeTypeComposite, p, existingField);
                     }
                  });

                  mappingMother.Add(otherChildrenTypeComposite.Id, motherNodeId);
               }
            }

            // Add mother node to refined nodes list
            var motherNode = new NodeTypeResolved(motherNodeTypeComposite, motherNodeTypeName);
            var motherClass = new NodeTypeRefinedMother(motherNode, unifiedResolver);
            refinedNodes.Add(motherClass);

            foreach (NodeTypeRefinedChild childrenNode in baseNode)
            {
               childrenNode.LinkedTo = motherClass;
            }
         }

         return mappingMother;
      }

      private static NodeTypeField TryFindExistingField(NodeTypeField p, NodeTypeComposite motherNodeTypeComposite)
      {
         NodeTypeField existingField = null;
         string propNameToSearch = p.Name;
         do
         {
            NodeTypeField foundedField = motherNodeTypeComposite.TypeFields.FirstOrDefault(x => x.Name == propNameToSearch);
            if (foundedField is not null)
            {
               existingField = foundedField;
               if(propNameToSearch is not null)
               {
                  propNameToSearch = RenamePropertyWithNewVersion(propNameToSearch);
               }
            }
            else
            {
               propNameToSearch = null;
            }
         } while (propNameToSearch is not null);

         return existingField;
      }

      private static void ManageNewPropertyVersion(
         List<NodeTypeRefined> refinedNodes, 
         NodeTypeComposite motherNodeTypeComposite,
         NodeTypeField currentField,
         NodeTypeField existingField)
      {
         NodeType existing = refinedNodes.First(x => x.Index == existingField.TypeId).NodeResolved.NodeType;
         NodeType current = refinedNodes.First(x => x.Index == currentField.TypeId).NodeResolved.NodeType;

         if(IsTypeChanged(existing, current) || IsPathChanged(existing, current))
         {
            currentField.Name = RenamePropertyWithNewVersion(existingField.Name);

            if (!DoesExistInMotherProp(motherNodeTypeComposite, currentField.Name))
            {
               AddPropToMotherTypeField(currentField, motherNodeTypeComposite);
            }
         } else
         {
            currentField.Name = existingField.Name;
         }
         //if (IsTypeChanged(existing, current) || (existing.Path is not null && current.Path is not null))
         //{
         //   string newPropName = RenamePropertyWithNewVersion(currentField.Name);
         //   if (IsPathChanged(existing, current) || IsTypeChanged(existing, current))
         //   {
         //      currentField.Name = newPropName;

         //      if (!DoesExistInMotherProp(motherNodeTypeComposite, newPropName))
         //      {
         //         AddPropToMotherTypeField(currentField, motherNodeTypeComposite);
         //      }
         //   }
         //}

         static bool IsPathChanged(NodeType n1, NodeType n2)
         {
            if (n1.Path is not null && n2.Path is null) { return true; }
            if (n1.Path is null && n2.Path is not null) { return true; }

            if (n1.Path is not null && n2.Path is not null)
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
            int num = Int32.Parse(propName[^1].ToString());
            return string.Join(string.Empty, propName.Take(propName.Length - 1)) + (num + 1);
         }
         else
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
         extensionProp.Add(new NodeTypeField()
         {
            Name = p.Name,
            Docs = p.Docs,
            TypeId = p.TypeId,
            TypeName = p.TypeName
         });
         motherNodeTypeComposite.TypeFields = extensionProp.ToArray();
      }

      private static string[] buildMotherPath(NodeTypeComposite childNodeTypeComposite)
      {
         string[] motherPath = new List<string>(childNodeTypeComposite.Path).ToArray();
         motherPath[motherPath.Count() - 1] = motherPath.Last() + "Base";
         return motherPath;
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

      private static void MapChildrenTypeToMotherType(
         List<NodeTypeRefined> refinedNodes,
         Dictionary<uint, ModuleAggregation> motherModules,
         IDictionary<uint, uint> mappingMother)
      {
         bool hasSomethingChanged = false;
         do
         {
            var mn = refinedNodes.Where(x => x.LevelNode == NodeTypeRefined.LevelTypeNode.Mother).ToDictionary(x => x.Index, x => x.NodeResolved.NodeType);
            hasSomethingChanged = MapSourceToDestination(mn, mappingMother);

            if (hasSomethingChanged)
            {
               RefineModules(motherModules.ToDictionary(x => x.Key, y => y.Value.AggregateModule), mappingMother);
            }
         } while (hasSomethingChanged);
      }

      private static void MapChildrenModuleTypeToMotherType(List<NodeTypeRefined> refinedNodes, IDictionary<uint, uint> mappingMother)
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

      private static void GenerateModules(string projectName, Dictionary<uint, PalletModule> modules, NodeTypeResolver typeDict, Dictionary<uint, NodeType> nodeTypes, string basePath, TypeModule typeModule = TypeModule.Version)
      {
         GenerateModules(projectName, modules.ToDictionary(x => x.Key, y => new ModuleAggregation() { AggregateModule = y.Value }), typeDict, nodeTypes, basePath, typeModule);
      }

      private static void GenerateModules(string projectName, Dictionary<uint, ModuleAggregation> modules, NodeTypeResolver typeDict, Dictionary<uint, NodeType> nodeTypes, string basePath, TypeModule typeModule = TypeModule.Version, IDictionary<uint, uint> mappingMother = null)
      {
         List<string> modulesResolved = new();
         foreach (ModuleAggregation module in modules.Values)
         {
            ModuleGenBuilder
                .Init(projectName, module.AggregateModule.Index, module.AggregateModule, typeDict, nodeTypes, typeModule, module.ModuleVersions, mappingMother)
                .Create()
                .Build(write: true, out bool _, basePath);

            modulesResolved.Add($"{module.AggregateModule.Name}Storage");
         }

         ClientBuilder
             .Init(projectName, 0, modulesResolved, typeDict).Create()
             .Build(write: true, out bool _, basePath);
      }
   }
}