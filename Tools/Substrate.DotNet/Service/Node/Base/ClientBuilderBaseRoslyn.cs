﻿using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.CodeDom;
using System.Collections.Generic;

namespace Substrate.DotNet.Service.Node.Base
{
   public abstract class ClientBuilderBaseRoslyn : BuilderBaseRoslyn
   {
      public List<string> ModuleNames { get; }

      protected ClientBuilderBaseRoslyn(string projectName, uint id, List<string> moduleNames, NodeTypeResolver typeDict)
          : base(projectName, id, typeDict)
      {
         ModuleNames = moduleNames;

         TargetUnit = TargetUnit.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName($"{ProjectName}.Generated.Storage")));
      }
   }
}