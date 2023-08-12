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
      //public string BlockHash { get; set; }
      public uint SpecVersion { get; set; }
   }
}
