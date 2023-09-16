using Substrate.NetApi.Model.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Client.Versions
{
   public class ModuleAggregation
   {
      public PalletModule AggregateModule { get; set; }
      public IList<ModuleVersion>? ModuleVersions { get; set; }
   }
}
