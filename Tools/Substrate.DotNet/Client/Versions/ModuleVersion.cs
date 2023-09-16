using Substrate.NetApi.Model.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Client.Versions
{
   public class ModuleVersion
   {
      public uint Version { get; set; }
      public PalletModule Module {  get; set; }
   }
}
