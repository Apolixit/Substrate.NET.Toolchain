using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Extensions
{
   public static class ArrayExtensions
   {
      public static void Push<T>(ref T[] table, object value)
      {
         Array.Resize(ref table, table.Length + 1);
         table.SetValue(value, table.Length - 1);
      }

      public static IEnumerable<T> AddIfNotExists<T>(
         this IEnumerable<T> list, IEnumerable<T> values, Func<IEnumerable<T>, T, bool> contains)
      {
         foreach(T value in values)
         {
            if (!contains(list, value))
            {
               return list.Append(value);
            }
         }
         
         return list;
      }
   }
}
