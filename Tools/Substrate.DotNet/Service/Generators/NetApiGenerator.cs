using Substrate.DotNet.Service.Generators.Base;
using Substrate.DotNet.Service.Node;
using Substrate.NetApi.Model.Meta;
using Serilog;
using System.Collections.Generic;
using Substrate.DotNet.Client.Versions;
using System;
using Microsoft.CodeAnalysis;
using System.Linq;

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
         Dictionary<uint, NodeType> refinedTypes = new();
         var resolvers = new List<NodeTypeResolver>();

         foreach (BlockVersion blockVersion in blockVersions)
         {
            GetGenericStructs(blockVersion.Metadata.NodeMetadata.Types);

            resolvers.Add(new NodeTypeResolver(NodeRuntime, ProjectName, blockVersion.Metadata.NodeMetadata.Types, null));
         }

         IEnumerable<KeyValuePair<uint, NodeTypeResolved>> x = resolvers
            .SelectMany(x => x.TypeNames)
            .Where(x => x.Value.Name.NamespaceSource == NodeTypeNamespaceSource.Generated);

         IEnumerable<IGrouping<string, KeyValuePair<uint, NodeTypeResolved>>> res = x.GroupBy(x => x.Value.Name.ToString());

         var resTest = res.Where(x => x.Count() < 3).ToList();
         var res2 = res.Where(x => x.Count() > 3).ToList();

         //GetGenericStructs(metadata.NodeMetadata.Types);
         //NodeTypeResolver typeDict = GenerateTypes(metadata.NodeMetadata.Types, _projectSettings.ProjectDirectory, write: true, blockVersion: blockVersion);
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