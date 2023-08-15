using Substrate.NetApi.Model.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Client.Versions
{
   public class BlockVersion
   {
      public uint BlockNumber { get; set; }
      public uint SpecVersion { get; set; }
      public MetaData Metadata { get; set; }
   }
}
