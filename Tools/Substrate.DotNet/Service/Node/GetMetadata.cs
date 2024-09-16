#nullable enable

using Serilog;
using Substrate.NET.Metadata.Base;
using Substrate.NET.Metadata.Service;
using Substrate.NET.Metadata.V14;
using Substrate.NetApi;
using Substrate.NetApi.Model.Extrinsics;
using Substrate.NetApi.Model.Meta;
using Substrate.NetApi.Model.Rpc;
using Substrate.NetApi.Model.Types.Base;
using Substrate.NetApi.Model.Types.Metadata;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Substrate.DotNet.Service.Node
{
   internal static class GetMetadata
   {
      internal static MetaData? GetMetadataFromFile(ILogger logger, string serviceArgument)
      {
         logger.Information("Loading metadata from file {file}...", serviceArgument);

         try
         {
            return GetMetadataFromSerializedText(logger, File.ReadAllText(serviceArgument));
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading metadata from file: {serviceArgument}.");
         }

         return null;
      }

      //internal static List<MetaData> GetMetadatasFromFiles(ILogger logger, List<string> serviceArguments)
      //{
      //   var res = new List<MetaData>();
      //   if(serviceArguments.Any())
      //   {
      //      foreach(string serviceArgument in serviceArguments)
      //      {
      //         MetaData? metadata = GetMetadataFromFile(logger, serviceArgument);
      //         if(metadata is not null)
      //         {
      //            res.Add(metadata);
      //         }
      //      }
      //   }

      //   return res;
      //}

      internal static string GetRuntimeFromFile(ILogger logger, string serviceArgument)
      {
         logger.Information("Loading runtime from file {file}...", serviceArgument);

         try
         {
            return File.ReadAllText(serviceArgument).Replace("-", "_");
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading runtime from file: {serviceArgument}.");
         }

         return string.Empty;
      }

      internal static MetaData? GetMetadataFromSerializedText(ILogger logger, string serializedText)
      {
         try
         {
            MetadataVersion version = MetadataUtils.GetMetadataVersion(serializedText);

            logger.Information("Found Metadata{version} => Conversion to V14", version);

            MetadataV14? v14 = null;
            switch(version)
            {
               case MetadataVersion.V9:
                  var v9 = new NET.Metadata.V9.MetadataV9(serializedText);
                  v14 = v9.ToMetadataV14();
                  break;
               case MetadataVersion.V10:
                  var v10 = new NET.Metadata.V10.MetadataV10(serializedText);
                  v14 = v10.ToMetadataV14();
                  break;
               case MetadataVersion.V11:
                  var v11 = new NET.Metadata.V11.MetadataV11(serializedText);

                  v14 = v11.ToMetadataV14();
                  break;
               case MetadataVersion.V12:
                  var v12 = new NET.Metadata.V12.MetadataV12(serializedText);
                  v14 = v12.ToMetadataV14();
                  break;
               case MetadataVersion.V13:
                  var v13 = new NET.Metadata.V13.MetadataV13(serializedText);
                  v14 = v13.ToMetadataV14();
                  break;
               case MetadataVersion.V14:
                  v14 = new NET.Metadata.V14.MetadataV14(serializedText);
                  break;
               default:
                  throw new InvalidOperationException($"Metadata version {version} is not supported!");
            }

            return v14.ToNetApiMetadata();
            //var runtimeMetadata = new RuntimeMetadata();
            //runtimeMetadata.Create(serializedText);
            //return new MetaData(runtimeMetadata, string.Empty);
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading metadata from serialized text!");
         }

         return null;
      }

      

      internal static async Task<uint?> GetBlockVersionFromNodeAsync(ILogger logger, string serviceArgument, uint? blockId, CancellationToken cancellationToken)
      {
         logger.Information("Loading version from node {node}...", serviceArgument);

         try
         {
            using var client = new SubstrateClient(new Uri(serviceArgument), ChargeTransactionPayment.Default());
            await client.ConnectAsync(true, cancellationToken);

            RuntimeVersion? version = null;
            if (blockId is null)
            {
               version = await client.State.GetRuntimeVersionAsync(cancellationToken);
            }
            else
            {
               Hash blockHash = await client.Chain.GetBlockHashAsync(new BlockNumber(blockId.Value), cancellationToken);
               version = await client.State.GetRuntimeVersionAsync(blockHash.Value, cancellationToken);
            }
            
            if(version is not null)
            {
               logger.Information("Version {version} successfully fetched...", version.SpecVersion);
               return version.SpecVersion;
            }   
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading metadata from node: {serviceArgument}.");
         }

         return null;
      }

      internal static async Task<string?> GetMetadataFromNodeAsync(ILogger logger, string serviceArgument, uint? blockId, CancellationToken cancellationToken)
      {
         logger.Information("Loading metadata from node {node} and block {blockId} ...", serviceArgument, blockId);

         try
         {
            using var client = new SubstrateClient(new Uri(serviceArgument), ChargeTransactionPayment.Default());
            await client.ConnectAsync(true, cancellationToken);

            if(blockId is null)
            {
               return await client.State.GetMetaDataAsync(cancellationToken);
            } else
            {
               Hash blockHash = await client.Chain.GetBlockHashAsync(new BlockNumber(blockId.Value), cancellationToken);
               return await client.State.GetMetaDataAsync(blockHash.Value, cancellationToken);
            }
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading metadata from node: {serviceArgument}.");
         }

         return null;
      }

      internal static async Task<string?> GetRuntimeFromNodeAsync(ILogger logger, string serviceArgument, CancellationToken cancellationToken)
      {
         logger.Information("Loading runtime from node {node}...", serviceArgument);

         try
         {
            using var client = new SubstrateClient(new Uri(serviceArgument), ChargeTransactionPayment.Default());
            await client.ConnectAsync(true, cancellationToken);
            return $"{client.RuntimeVersion.SpecName}_runtime";
         }
         catch (Exception ex)
         {
            logger.Error(ex, $"Error while loading metadata from node: {serviceArgument}.");
         }

         return null;
      }
   }
}