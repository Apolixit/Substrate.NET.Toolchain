using Substrate.NetApi.Model.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Extensions
{
   public static class MetadataTypeExtensions
   {
      public static IEnumerable<NodeTypeField> Copy(this IEnumerable<NodeTypeField> originalList)
      {
         if(originalList is null)
         {
            return Enumerable.Empty<NodeTypeField>();
         }

         return originalList.Select(o => new NodeTypeField()
         {
            Docs = o.Docs,
            Name = o.Name,
            TypeId = o.TypeId,
            TypeName = o.TypeName
         });
      }

      public static IEnumerable<NodeTypeParam> Copy(this IEnumerable<NodeTypeParam> originalList)
      {
         if (originalList is null)
         {
            return Enumerable.Empty<NodeTypeParam>();
         }

         return originalList.Select(o => new NodeTypeParam()
         {
            Name = o.Name,
            TypeId = o.TypeId,
         });
      }
   }
}
