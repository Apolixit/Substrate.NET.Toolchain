using Substrate.DotNet.Service.Generators.Base;
using Substrate.DotNet.Service.Node;
using Substrate.NetApi.Model.Meta;
using Serilog;
using System.Collections.Generic;
using Substrate.DotNet.Client.Versions;
using System;

namespace Substrate.DotNet.Service.Generators
{
   /// <summary>
   /// Responsible for generating the RestService Solution
   /// </summary>
   public class RestServiceGenerator : SolutionGeneratorBase
   {
      private readonly ProjectSettings _projectSettings;

      public RestServiceGenerator(ILogger logger, string nodeRuntime, string netApiProjectName, ProjectSettings projectSettings)
         : base(logger, nodeRuntime, netApiProjectName)
      {
         // Rest Service project configuration.
         _projectSettings = projectSettings;
      }

      protected override void GenerateClasses(List<BlockVersion> blockVersions)
      {
         // TODO Romain : implement it
         throw new NotImplementedException();
      }

      protected override void GenerateClasses(MetaData metadata, BlockVersion? blockVersion = null)
      {
         SolutionGeneratorBase.GetGenericStructs(metadata.NodeMetadata.Types);

         // Generate types as if we were generating them for Types project but just keep them in memory
         // so we can reference these types and we don't output all the types while generating the rest service.
         NodeTypeResolver typeDict = GenerateTypes(metadata.NodeMetadata.Types, string.Empty, write: false);

         foreach (PalletModule module in metadata.NodeMetadata.Modules.Values)
         {
            RestServiceStorageModuleBuilder
                .Init(_projectSettings.ProjectName, module.Index, module, typeDict, metadata.NodeMetadata.Types)
                .Create()
                .Build(write: true, out bool _, basePath: _projectSettings.ProjectDirectory);

            RestServiceControllerModuleBuilder
                .Init(_projectSettings.ProjectName, ProjectName, module.Index, module, typeDict, metadata.NodeMetadata.Types)
                .Create()
                .Build(write: true, out bool _, basePath: _projectSettings.ProjectDirectory);
         }
      }

   }
}