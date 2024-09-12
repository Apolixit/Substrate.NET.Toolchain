using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Substrate.DotNet.Service.Node.Base;
using Substrate.NetApi.Model.Meta;
using Substrate.NetApi.Model.Types;
using System;
using System.CodeDom;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using static Substrate.NetApi.Model.Meta.Storage;
using Serilog;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Substrate.NET.Metadata.Base;
using Newtonsoft.Json.Linq;

namespace Substrate.DotNet.Service.Node
{
   public class EnumBuilder : TypeBuilderBase
   {
      private EnumBuilder(string projectName, uint id, NodeTypeVariant typeDef, NodeTypeResolver typeDict)
          : base(projectName, id, typeDef, typeDict)
      {
      }

      public static EnumBuilder Init(string projectName, uint id, NodeTypeVariant typeDef, NodeTypeResolver typeDict)
      {
         return new EnumBuilder(projectName, id, typeDef, typeDict);
      }

      public override TypeBuilderBase Create()
      {
         var typeDef = TypeDef as NodeTypeVariant;
         string enumName = $"{typeDef.Path.Last()}";

         if (!Resolver.TypeNames.TryGetValue(Id, out NodeTypeResolved nodeTypeResolved))
         {
            throw new NotSupportedException($"Could not find type {Id}");
         }

         ClassName = nodeTypeResolved.ClassName;

         ReferenzName = $"{NamespaceName}.{ClassName}";

         NamespaceDeclarationSyntax typeNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(NamespaceName));

         // only add the enumeration in the first variations.
         if (string.IsNullOrEmpty(nodeTypeResolved.Name.ClassNameSufix) || nodeTypeResolved.Name.ClassNameSufix == "1")
         {
               EnumDeclarationSyntax targetType = SyntaxFactory
                  .EnumDeclaration(enumName)
                  .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

               if (typeDef.Variants != null)
               {
                  foreach (TypeVariant variant in typeDef.Variants)
                  {
                     targetType = targetType.AddMembers(
                         SyntaxFactory.EnumMemberDeclaration(EscapeIfKeyword(variant.Name))
                           .WithEqualsValue(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(variant.Index))))
                     );
                  }
               }
               typeNamespace = typeNamespace.AddMembers(targetType);
         }

         ClassDeclarationSyntax targetClass = SyntaxFactory.ClassDeclaration(ClassName)
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword));

         targetClass = targetClass.WithLeadingTrivia(GetCommentsRoslyn(typeDef.Docs, typeDef));

         if (typeDef.Variants == null || typeDef.Variants.All(p => p.TypeFields == null))
         {
            targetClass = targetClass.AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName($"BaseEnum<{enumName}>")));
            typeNamespace = typeNamespace.AddMembers(targetClass);
         }
         else
         {
            targetClass = targetClass.AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName($"BaseEnumRust<{enumName}>")));

            var genericTypeArguments = new List<TypeSyntax> { SyntaxFactory.ParseTypeName(enumName) };

            ConstructorDeclarationSyntax constructor = SyntaxFactory.ConstructorDeclaration(ClassName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

            //constructor = constructor.WithBody(
            //   SyntaxFactory.Block(CreateMethodInvocation("Substrate.NetApiExt.Generated.Model.frame_support.dispatch.DispatchInfo", "h", "ExtrinsicSuccess")));
            //.WithBody(
            //    SyntaxFactory.Block(
            //        CreateMethodInvocation("Substrate.NetApiExt.Generated.Model.frame_support.dispatch.DispatchInfo", "ExtrinsicSuccess"),
            //        CreateMethodInvocation("BaseTuple<Substrate.NetApiExt.Generated.Model.sp_runtime.EnumDispatchError, Substrate.NetApiExt.Generated.Model.frame_support.dispatch.DispatchInfo>", "ExtrinsicFailed"),
            //        CreateMethodInvocation("BaseVoid", "CodeUpdated"),
            //        CreateMethodInvocation("Substrate.NetApiExt.Generated.Model.sp_core.crypto.AccountId32", "NewAccount"),
            //        CreateMethodInvocation("Substrate.NetApiExt.Generated.Model.sp_core.crypto.AccountId32", "KilledAccount"),
            //        CreateMethodInvocation("BaseTuple<Substrate.NetApiExt.Generated.Model.sp_core.crypto.AccountId32, Substrate.NetApiExt.Generated.Model.primitive_types.H256>", "Remarked"),
            //        CreateMethodInvocation("BaseTuple<Substrate.NetApiExt.Generated.Model.primitive_types.H256, Substrate.NetApi.Model.Types.Primitive.Bool>", "UpgradeAuthorized")
            //    )
            //);

            foreach (TypeVariant variant in typeDef.Variants)
            {
               string decoderType;
               if (variant.TypeFields == null || variant.TypeFields.Length == 0)
               {
                  decoderType = "BaseVoid";
               }
               else if (variant.TypeFields.Length == 1)
               {
                  NodeTypeResolved item = GetFullItemPath(variant.TypeFields[0].TypeId);
                  decoderType = item.ToString();
               }
               else
               {
                  string tupleType = $"BaseTuple<{string.Join(", ", variant.TypeFields.Select(f => GetFullItemPath(f.TypeId)))}>";
                  decoderType = tupleType;
               }

               constructor = constructor.WithBody(
                  SyntaxFactory.Block(
                     CreateMethodInvocation(decoderType, enumName, HandleReservedKeyword(variant.Name))
                     )
                  );
            }

            targetClass = targetClass.AddMembers(constructor);

            //GenericNameSyntax baseEnumExt = SyntaxFactory.GenericName(SyntaxFactory.Identifier("BaseEnumRust"), SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(genericTypeArguments)));
            //targetClass = targetClass.AddBaseListTypes(SyntaxFactory.SimpleBaseType(baseEnumExt));
            typeNamespace = typeNamespace.AddMembers(targetClass);
         }

         TargetUnit = TargetUnit.AddMembers(typeNamespace);

         return this;
      }

      static ExpressionStatementSyntax CreateMethodInvocation(string genericType, string enumName, string enumValue)
      {
         return SyntaxFactory.ExpressionStatement(
             SyntaxFactory.InvocationExpression(
                 SyntaxFactory.GenericName("AddTypeDecoder")  // Method name
                     .WithTypeArgumentList(                  // Add the generic argument (like AddTypeDecoder<Type>)
                         SyntaxFactory.TypeArgumentList(
                             SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                                 SyntaxFactory.ParseTypeName(genericType)
                             )
                         )
                     )
             )
             .WithArgumentList(                             // Argument list (like AddTypeDecoder<Type>(Event.Value))
                 SyntaxFactory.ArgumentList(
                     SyntaxFactory.SingletonSeparatedList(
                         SyntaxFactory.Argument(
                             SyntaxFactory.MemberAccessExpression(
                                 SyntaxKind.SimpleMemberAccessExpression,
                                 SyntaxFactory.IdentifierName(enumName),       // Event
                                 SyntaxFactory.IdentifierName(enumValue)      // Event.Value
                             )
                         )
                     )
                 )
             )
         );
      }

      public static string HandleReservedKeyword(string name)
      {
         // List of C# reserved keywords
         var reservedKeywords = new HashSet<string>
          {
              "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
              "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
              "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
              "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
              "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly",
              "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
              "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
              "using", "virtual", "void", "volatile", "while"
          };

         // If the name is a reserved keyword, prepend with @
         return reservedKeywords.Contains(name) ? $"@{name}" : name;
      }
   }
}