using Substrate.DotNet.Client;
using Substrate.DotNet.Service.Generators;
using Substrate.DotNet.Service.Node;
using Substrate.NetApi.Model.Meta;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Serilog;
using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Substrate.DotNet.Service.Generators.Base;
using System.Linq;
using System.Collections.Generic;
using Substrate.NetApi.Model.Rpc;
using System.Collections;
using Microsoft.CodeAnalysis;
using Substrate.DotNet.Client.Versions;

namespace Substrate.DotNet
{
   partial class Program
   {
      /// <summary>
      /// Command line utility to easily maintain and scaffold Substrate .NET Toolchain related projects.
      /// 
      /// Usage
      /// dotnet substrate update
      /// 
      /// </summary>
      static async Task Main(string[] args)
      {
         // Initialize logging.
         Log.Logger = new LoggerConfiguration()
          .MinimumLevel.Verbose()
          .WriteTo.Console()
          .CreateLogger();

         try
         {
            for (int i = 0; i < args.Length; i++)
            {
               args[i] = "upgrade";
               switch (args[i])
               {
                  // Handles dotnet substrate update
                  case "update":
                     {
                        if (!await UpdateSubstrateEnvironmentAsync(CancellationToken.None))
                        {
                           Log.Error("Updating project did not complete successfully.");
                           Environment.Exit(-1);
                        }
                     }
                     break;

                  // Handles dotnet substrate upgrade
                  case "upgrade":
                     {
                        if (!await UpgradeSubstrateEnvironmentAsync(CancellationToken.None))
                        {
                           Log.Error("Upgrading project did not complete successfully.");
                           Environment.Exit(-1);
                        }
                     }
                     break;

                  default:
                     break;
               }
            }
         }
         catch (InvalidOperationException ex)
         {
            Log.Error(ex, "Could not complete operation!");
            Environment.Exit(-1);
         }
         catch (Exception ex)
         {
            Log.Error(ex, "Unhandled exception!");
            Environment.Exit(-1);
         }

      }

      /// <summary>
      /// Invoked with dotnet substrate update
      /// This command parses the substrate project configuration and generates code for all given projects.
      /// </summary>
      /// <returns>Returns true on success, otherwise false.</returns>
      private static async Task<bool> UpdateSubstrateEnvironmentAsync(CancellationToken token) => await UpgradeOrUpdateSubstrateEnvironmentAsync(fetchMetadata: false, token);

      /// <summary>
      /// Invoked with dotnet substrate upgrade.
      /// This command first updates the metadata file and then generates all classes again.
      /// </summary>
      /// <returns>Returns true on success, otherwise false.</returns>
      private static async Task<bool> UpgradeSubstrateEnvironmentAsync(CancellationToken token) => await UpgradeOrUpdateSubstrateEnvironmentAsync(fetchMetadata: true, token);

      /// <summary>
      /// Handles the implementation to update or upgrade an substrate environment
      /// Upgrading first fetches the metadata and stores it in .substrate configuration directory.
      /// Then a normal update command is invoked to generate code for all given projects.
      /// </summary>
      /// <param name="token">Cancellation</param>
      /// <param name="fetchMetadata">Controls whether to fetch the metadata (upgrade) or not (update).</param>
      /// <returns>Returns true on success, otherwise false.</returns>
      private static async Task<bool> UpgradeOrUpdateSubstrateEnvironmentAsync(bool fetchMetadata, CancellationToken token)
      {
         // Update an existing substrate project tree by reading the required configuration file
         // in the current directory in subdirectory .substrate.
         string configurationFile = ResolveConfigurationFilePath();
         if (!File.Exists(configurationFile))
         {
            Log.Error("The configuration file {file} does not exist! Please create a configuration file so this toolchain can produce correct outputs. You can scaffold the configuration file by creating a new service project with `dotnet new substrate-service`.");
            return false;
         }

         // Read substrate-config.json
         SubstrateConfiguration configuration = JsonConvert.DeserializeObject<SubstrateConfiguration>(File.ReadAllText(configurationFile));
         if (configuration == null)
         {
            Log.Error("Could not parse the configuration file {file}! Please ensure that the configuration file format is correct.", configurationFile);
            return false;
         }

         Log.Information("Using NetApi Project = {name}", configuration.Projects.NetApi);
         Log.Information("Using RestService Project = {name}", configuration.Projects.RestService);
         Log.Information("Using RestClient Project = {name}", configuration.Projects.RestClient);
         Log.Information("Using RestClient.Mockup Project = {name}", configuration.Projects.RestClientMockup);
         Log.Information("Using RestClient.Test Project = {name}", configuration.Projects.RestClientTest);
         Log.Information("Using RestService assembly for RestClient = {assembly}", configuration.RestClientSettings.ServiceAssembly);

         IEnumerable<BlockVersion> uniqueBlockVersion = Enumerable.Empty<BlockVersion>();
         if (HasMultiVersion(configuration))
         {
            Log.Information("Fetching multiple metadata from blocks = [{blocks}]", string.Join(", ", configuration.Metadata.FromBlocks));

            uniqueBlockVersion = await EnsureDistinctBlockVersionAsync(configuration.Metadata.Websocket, configuration.Metadata.FromBlocks, token);
         }

         if (fetchMetadata)
         {
            Log.Information("Using Websocket = {websocket} to fetch metadata", configuration.Metadata.Websocket);

            if (HasMultiVersion(configuration))
            {
               foreach (BlockVersion blockVersion in uniqueBlockVersion)
               {
                  if (!await GenerateMetadataAsync(configuration.Metadata.Websocket, blockVersion, token))
                  {
                     Log.Error("Unable to fetch metadata from websocket {websocket}. Aborting.", configuration.Metadata.Websocket);
                     return false;
                  }
               }
            }
            else
            {
               if (!await GenerateMetadataAsync(configuration.Metadata.Websocket, null, token))
               {
                  Log.Error("Unable to fetch metadata from websocket {websocket}. Aborting.", configuration.Metadata.Websocket);
                  return false;
               }
            }

            if (!await GenerateRuntimeAsync(configuration.Metadata.Websocket, token))
            {
               Log.Error("Unable to fetch runtime from websocket {websocket}. Aborting.", configuration.Metadata.Websocket);
               return false;
            }
         }

         string runtimeFilePath = ResolveRuntimeFilePath();
         Log.Information("Using Runtime = {runtimeFilePath}", runtimeFilePath);

         configuration.Metadata.Runtime = GetMetadata.GetRuntimeFromFile(Log.Logger, runtimeFilePath);
         if (string.IsNullOrEmpty(configuration.Metadata.Runtime))
         {
            return false;
         }

         Log.Information("Using Runtime {runtime}", configuration.Metadata.Runtime);

         if (HasMultiVersion(configuration))
         {
            uniqueBlockVersion.ToList().ForEach(b => b.Metadata = ManageMetadata(b));
            GenerateNetApiClasses(configuration, uniqueBlockVersion.ToList());

            //foreach (BlockVersion blockVersion in uniqueBlockVersion)
            //{
            //   MetaData metadata = ManageMetadata(blockVersion);
            //   GenerateNetApiClasses(metadata, configuration, blockVersion);
            //}
         }
         else
         {
            MetaData metadata = ManageMetadata(null);
            
            if (metadata == null)
            {
               return false;
            }

            if (configuration.Metadata.IsMetadataRefined)
            {
               Log.Information("MetaData refined option is activated");
               SolutionGeneratorBase.RefineVecWrapper(metadata);
            }

            // Service
            GenerateNetApiClasses(metadata, configuration);
            GenerateRestServiceClasses(metadata, configuration);

            // Client
            GenerateRestClientClasses(configuration);
         }

         return true;
      }

      private static MetaData? ManageMetadata(BlockVersion? blockVersion)
      {
         string metadataFilePath = (blockVersion is null) ? ResolveMetadataFilePath() : ResolveMetadataFilePath(blockVersion.SpecVersion);
         Log.Information("Using Metadata = {metadataFilePath}", metadataFilePath);

         MetaData metadata = GetMetadata.GetMetadataFromFile(Log.Logger, metadataFilePath);
         if (metadata == null)
         {
            return null;
         }

         // write metadata
         string metadataJsonFilePath = (blockVersion is null) ? ResolveMetadataJsonFilePath() : ResolveMetadataJsonFilePath(blockVersion.SpecVersion);
         Log.Information("Using MetadataJson = {metadataJsonFilePath}", metadataJsonFilePath);
         File.WriteAllText(metadataJsonFilePath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

         return metadata;
      }

      private static bool HasMultiVersion(SubstrateConfiguration configuration)
      {
         return configuration.Metadata.FromBlocks is not null && configuration.Metadata.FromBlocks.Any();
      }

      /// <summary>
      /// Fetches and generates .substrate/metadata.txt
      /// </summary>
      /// <param name="websocket">The websocket to connect to</param>
      /// <param name="token">Cancellation token.</param>
      /// <returns>Returns true on success, otherwise false.</returns>
      /// <exception cref="InvalidOperationException"></exception>
      private static async Task<bool> GenerateMetadataAsync(string websocket, BlockVersion? blockVersion, CancellationToken token)
      {
         string metadata = await GetMetadata.GetMetadataFromNodeAsync(Log.Logger, websocket, blockVersion?.BlockNumber, token);
         if (metadata == null)
         {
            throw new InvalidOperationException($"Could not query metadata from node {websocket}!");
         }

         string metadataFilePath = (blockVersion is null) ? ResolveMetadataFilePath() : ResolveMetadataFilePath(blockVersion.SpecVersion);

         try
         {
            Log.Information("Saving metadata to {metadataFilePath}...", metadataFilePath);
            File.WriteAllText(metadataFilePath, metadata);
            return true;
         }
         catch (Exception e)
         {
            Log.Error(e, $"Could not save metadata to filepath: {metadataFilePath}!");
         }

         return false;
      }

      /// <summary>
      /// Fetches and generates .substrate/metadata.txt
      /// </summary>
      /// <param name="websocket">The websocket to connect to</param>
      /// <param name="token">Cancellation token.</param>
      /// <returns>Returns true on success, otherwise false.</returns>
      /// <exception cref="InvalidOperationException"></exception>
      private static async Task<IEnumerable<BlockVersion>> EnsureDistinctBlockVersionAsync(string websocket, IEnumerable<uint> blocksId, CancellationToken token)
      {
         // Before fetching metadata, check if version changed between blocks (otherwise, no need to generate multiple identical metadata files
         var uniqueBlockVersion = new List<BlockVersion>();
         foreach (uint blockId in blocksId)
         {
            uint? version = await GetMetadata.GetBlockVersionFromNodeAsync(Log.Logger, websocket, blockId, token);
            if (version is null)
            {
               throw new InvalidOperationException($"Could not query version from node {websocket} and block {blockId} !");
            }

            uint? existingVersion = uniqueBlockVersion.FirstOrDefault(x => x.SpecVersion == version)?.BlockNumber;
            if (existingVersion is null)
            {
               var newVersion = new BlockVersion()
               {
                  BlockNumber = blockId,
                  SpecVersion = version.Value
               };
               //newVersion.Metadata = ManageMetadata(newVersion);
               uniqueBlockVersion.Add(newVersion);
            }
            else
            {
               Log.Warning("Current blockId = {blockId} has SpecVersion = {specVersion} already has this version in blockId = {otherBlockId}, metadata file is not duplicated", blockId, version.Value, existingVersion);
            }
         }

         return uniqueBlockVersion;
      }

      /// <summary>
      /// Fetches and generates .substrate/runtime.txt
      /// </summary>
      /// <param name="websocket">The websocket to connect to</param>
      /// <param name="token">Cancellation token.</param>
      /// <returns>Returns true on success, otherwise false.</returns>
      /// <exception cref="InvalidOperationException"></exception>
      private static async Task<bool> GenerateRuntimeAsync(string websocket, CancellationToken token)
      {
         string runtime = await GetMetadata.GetRuntimeFromNodeAsync(Log.Logger, websocket, token);
         if (runtime == null)
         {
            throw new InvalidOperationException($"Could not query runtime from node {websocket}!");
         }

         string runtimeFilePath = ResolveRuntimeFilePath();

         try
         {
            Log.Information("Saving runtime to {runtimeFilePath}...", runtimeFilePath);
            File.WriteAllText(runtimeFilePath, runtime);
            return true;
         }
         catch (Exception e)
         {
            Log.Error(e, $"Could not save runtime to filepath: {runtimeFilePath}!");
         }

         return false;
      }

      /// <summary>
      /// Generates all classes for the RestService project
      /// </summary>
      private static void GenerateRestServiceClasses(MetaData metadata, SubstrateConfiguration configuration)
      {
         var generator = new RestServiceGenerator(Log.Logger, configuration.Metadata.Runtime, configuration.Projects.NetApi, new ProjectSettings(configuration.Projects.RestService));
         generator.Generate(metadata);
      }

      private static void GenerateRestClientClasses(SubstrateConfiguration configuration)
      {
         string filePath = ResolveRestServiceAssembly(configuration);
         if (string.IsNullOrEmpty(filePath))
         {
            Log.Information("Could not resolve RestService assembly file path. Please build the RestService before generating RestClient project classes.");
            return;
         }

         Log.Information("Using resolved RestService assembly for RestClient = {assembly}", filePath);

         using var loader = new AssemblyResolver(filePath);

         // Initialize configuration.
         var clientConfiguration = new ClientGeneratorConfiguration()
         {
            Assembly = loader.Assembly,
            ControllerBaseType = typeof(ControllerBase),
            OutputDirectory = Path.Join(Environment.CurrentDirectory, configuration.Projects.RestClient),
            GeneratorOptions = new CodeGeneratorOptions()
            {
               BlankLinesBetweenMembers = false,
               BracingStyle = "C",
               IndentString = "   "
            },
            BaseNamespace = configuration.Projects.RestClient
         };

         // Build and execute the client generator.
         var client = new ClientGenerator(clientConfiguration);
         client.Generate(Log.Logger);

         // Mockup client.
         clientConfiguration.OutputDirectory = Path.Join(Environment.CurrentDirectory, configuration.Projects.RestClientMockup);
         clientConfiguration.BaseNamespace = configuration.Projects.RestClientMockup;
         clientConfiguration.ClientClassname = "MockupClient";

         var mockupClient = new MockupClientGenerator(clientConfiguration);
         mockupClient.Generate(Log.Logger);

         // Unit test.
         clientConfiguration.OutputDirectory = Path.Join(Environment.CurrentDirectory, configuration.Projects.RestClientTest);
         clientConfiguration.BaseNamespace = configuration.Projects.RestClientTest;
         clientConfiguration.ClientClassname = string.Empty;

         var unitTestClient = new UnitTestGenerator(clientConfiguration);
         unitTestClient.Generate(Log.Logger);
      }

      /// <summary>
      /// Generates all classes for the NetApi project
      /// </summary>
      private static void GenerateNetApiClasses(MetaData metadata, SubstrateConfiguration configuration, BlockVersion? blockVersion = null)
      {
         var generator = new NetApiGenerator(Log.Logger, configuration.Metadata.Runtime, new ProjectSettings(configuration.Projects.NetApi));
         generator.Generate(metadata, blockVersion);
      }

      private static void GenerateNetApiClasses(
         SubstrateConfiguration configuration,
         List<BlockVersion> blockVersions)
      {
         var generator = new NetApiGenerator(Log.Logger, configuration.Metadata.Runtime, new ProjectSettings(configuration.Projects.NetApi));
         generator.Generate(blockVersions);
      }

      /// <summary>
      /// Returns the directory path to .substrate directory
      /// </summary>
      private static string ResolveConfigurationDirectory() => Path.Join(Environment.CurrentDirectory, ".substrate");

      /// <summary>
      /// Returns the file path to .substrate/substrate-config.json
      /// </summary>
      private static string ResolveConfigurationFilePath() => Path.Join(ResolveConfigurationDirectory(), "substrate-config.json");

      /// <summary>
      /// Returns the file path to .substrate/metadata.txt
      /// </summary>
      private static string ResolveMetadataFilePath() => Path.Join(ResolveConfigurationDirectory(), "metadata.txt");
      private static string ResolveMetadataFilePath(uint suffix) => Path.Join(ResolveConfigurationDirectory(), $"metadata_{suffix}.txt");

      /// <summary>
      /// Returns the file path to .substrate/metadata.json
      /// </summary>
      private static string ResolveMetadataJsonFilePath() => Path.Join(ResolveConfigurationDirectory(), "metadata.json");
      private static string ResolveMetadataJsonFilePath(uint suffix) => Path.Join(ResolveConfigurationDirectory(), $"metadata_{suffix}.json");

      /// <summary>
      /// Returns the file path to .substrate/runtime.txt
      /// </summary>
      private static string ResolveRuntimeFilePath() => Path.Join(ResolveConfigurationDirectory(), "runtime.txt");

      private static string ResolveRestServiceAssembly(SubstrateConfiguration configuration)
      {
         if (File.Exists(configuration.RestClientSettings.ServiceAssembly))
         {
            return configuration.RestClientSettings.ServiceAssembly;
         }

         string framework = $"net{Environment.Version.Major}.{Environment.Version.Minor}";
         string fp = ResolveServicePath(framework, "Release", configuration.Projects.RestService, configuration.RestClientSettings.ServiceAssembly);

         if (File.Exists(fp))
         {
            return fp;
         }
         else
         {
            Log.Information("The file path {path} does not exist.", fp);
         }

         // Check if Debug version exist (if Release isn't available)
         fp = ResolveServicePath(framework, "Debug", configuration.Projects.RestService, configuration.RestClientSettings.ServiceAssembly);
         if (File.Exists(fp))
         {
            return fp;
         }
         else
         {
            Log.Information("The file path {path} does not exist.", fp);
         }

         return string.Empty;
      }

      private static string ResolveServicePath(string framework, string configuration, string restServiceProject, string assembly)
      {
         return Path.Combine(
            ResolveConfigurationDirectory(),
            "..",
            restServiceProject,
            "bin",
            configuration,
            framework,
            assembly);
      }
   }
}
