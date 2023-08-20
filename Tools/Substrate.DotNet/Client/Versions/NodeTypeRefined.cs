using Substrate.DotNet.Service.Node;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Substrate.DotNet.Client.Versions
{
   public abstract class NodeTypeRefined
   {
      protected NodeTypeRefined() { }

      protected NodeTypeRefined(NodeTypeResolved typeNode, NodeTypeResolver typeNodeResolver)
      {
         NodeResolved = typeNode;
         Resolver = typeNodeResolver;
      }

      public uint Index => NodeResolved.NodeType.Id;
      public string Version => Resolver.ProjectSpecVersion;
      public NodeTypeResolved NodeResolved { get; protected set; }
      public NodeTypeResolver Resolver { get; set; }
      public abstract LevelTypeNode LevelNode { get; }
      public enum LevelTypeNode
      {
         Mother,
         Child
      }
   }

   public class NodeTypeRefinedChild : NodeTypeRefined
   {
      protected NodeTypeRefinedChild() { }

      public NodeTypeRefinedChild(NodeTypeResolved typeResolved, NodeTypeRefinedMother? linkedTo, NodeTypeResolver typeNodeResolver) : base(typeResolved, typeNodeResolver)
      {
         LinkedTo = linkedTo;
      }

      public NodeTypeRefinedMother? LinkedTo { get; set; }
      public override LevelTypeNode LevelNode => LevelTypeNode.Child;
   }

   public class NodeTypeRefinedMother : NodeTypeRefined
   {
      protected NodeTypeRefinedMother() { }

      public NodeTypeRefinedMother(NodeTypeResolved typeResolved, NodeTypeResolver typeNodeResolver) : base(typeResolved, typeNodeResolver)
      {
      }

      public override LevelTypeNode LevelNode => LevelTypeNode.Mother;
   }
}
