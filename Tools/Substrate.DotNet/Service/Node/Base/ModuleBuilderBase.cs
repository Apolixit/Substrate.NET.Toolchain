using Substrate.DotNet.Client.Versions;
using Substrate.DotNet.Extensions;
using Substrate.NetApi.Model.Meta;
using System.Collections.Generic;

namespace Substrate.DotNet.Service.Node.Base
{
   public abstract class ModuleBuilderBase : BuilderBase
   {
      public Dictionary<uint, NodeType> NodeTypes { get; private set; }

      public PalletModule Module { get; private set; }

      public string PrefixName { get; private set; }
      public string ProjectSpecVersion { get; private set; }

      protected ModuleBuilderBase(string projectName, uint id, PalletModule module, NodeTypeResolver typeDict,
          Dictionary<uint, NodeType> nodeTypes)
          : base(projectName, id, typeDict)
      {
         NodeTypes = nodeTypes;
         Module = module;
         PrefixName = module.Name == "System" ? "Frame" : "Pallet";

         ProjectSpecVersion = typeDict.ProjectSpecVersion;

         string suffixVersion = string.IsNullOrEmpty(ProjectSpecVersion) ? string.Empty : $"{ProjectSpecVersion}.";
         NamespaceName = $"{ProjectName}.Generated.Model.{suffixVersion}{PrefixName + module.Name.MakeMethod()}";
      }
   }
}