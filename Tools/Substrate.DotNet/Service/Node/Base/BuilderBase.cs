﻿using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Substrate.NetApi.Model.Meta;
using System;
using Serilog;
using System.Net.NetworkInformation;

namespace Substrate.DotNet.Service.Node.Base
{
   public abstract class BuilderBase
   {
      protected static readonly List<string> Files = new();

      public uint Id { get; }

      protected NodeTypeResolver Resolver { get; }

      public bool Success { get; set; }

      public string NamespaceName { get; protected set; }

      internal string FileName { get; set; }

      public string ClassName { get; set; }

      public string ReferenzName { get; set; }

      public string ProjectName { get; private set; }

      public CompilationUnitSyntax TargetUnit { get; set; }

      public abstract BuilderBase Create();

      protected BuilderBase(string projectName, uint id, NodeTypeResolver resolver)
      {
         ProjectName = projectName;
         Id = id;
         Resolver = resolver;
         TargetUnit = SyntaxFactory.CompilationUnit()
            .AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Substrate.NetApi.Model.Types.Base")),
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic")));
         Success = true;
      }

      public static string EscapeIfKeyword(string parameterName)
      {
         if (SyntaxFacts.GetKeywordKind(parameterName) != SyntaxKind.None)
         {
            // If it is a keyword, create a verbatim identifier which adds '@' prefix
            parameterName = "@" + parameterName;
         }

         return parameterName;
      }

      public NodeTypeResolved GetFullItemPath(uint typeId)
      {
         if (!Resolver.TypeNames.TryGetValue(typeId, out NodeTypeResolved fullItem))
         {
            Success = false;
            return null;
         }

         return fullItem;
      }

      public virtual void Build(bool write, out bool success, string basePath = null)
      {
         success = Success;
         if (write && Success)
         {
            string path = GetPath(basePath);

            Directory.CreateDirectory(Path.GetDirectoryName(path));

            if (!Files.Contains(path))
            {
               Files.Add(path);
            }
            else
            {
               Log.Warning($"Overwriting[BUG]: {path}");
            }

            // add autogenerated header
            TargetUnit = TargetUnit.WithLeadingTrivia(SyntaxFactory.TriviaList(HeaderComment));

            using StreamWriter sourceWriter = new(path);
            sourceWriter.Write(TargetUnit.NormalizeWhitespace().ToFullString());
         }
      }

      private string GetPath(string basePath)
      {
         var space = NamespaceName.Split('.').ToList();

         space.Add((FileName is null ? ClassName : FileName) + ".cs");

         // Remove the first two parts of the namespace to avoid the files being created in the Substrate/NetApi sub folder.
         space = space.TakeLast(space.Count - 2).ToList();

         // Add base path at the beginning of the paths list
         if (!string.IsNullOrEmpty(basePath))
         {
            space.Insert(0, basePath);
         }

         string path = Path.Combine(space.ToArray());

         return path;
      }

      protected ClassDeclarationSyntax AddTargetClassCustomAttributesRoslyn(ClassDeclarationSyntax targetClass, NodeType typeDef)
      {
         // TODO (svnscha): Change version to given metadata version.
         TargetUnit = TargetUnit.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Substrate.NetApi.Model.Types.Metadata.V14")),
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Substrate.NetApi.Attributes")));

         AttributeArgumentSyntax attributeArgument = SyntaxFactory.AttributeArgument(
             SyntaxFactory.ParseExpression($"TypeDefEnum.{typeDef.TypeDef}"));

         AttributeListSyntax attributeList = SyntaxFactory.AttributeList(
             SyntaxFactory.SingletonSeparatedList(
                 SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("SubstrateNodeType"), SyntaxFactory.AttributeArgumentList(SyntaxFactory.SingletonSeparatedList(attributeArgument)))));

         return targetClass.AddAttributeLists(attributeList);
      }

      public static MethodDeclarationSyntax SimpleMethodRoslyn(string name, string returnType = null, object returnExpression = null)
      {
         MethodDeclarationSyntax nameMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(returnType ?? "void"), name)
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.OverrideKeyword));

         if (returnType != null && returnExpression != null)
         {
            nameMethod = nameMethod.WithBody(SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(SyntaxFactory.ParseExpression($"\"{returnExpression}\""))
            ));
         }

         return nameMethod;
      }

      public static SyntaxTriviaList GetCommentsRoslyn(string[] docs, NodeType typeDef = null, string typeName = null)
      {
         var commentList = new List<SyntaxTrivia>
         {
            SyntaxFactory.Comment("/// <summary>")
         };

         if (typeDef != null)
         {
            string path = typeDef.Path != null ? "[" + string.Join('.', typeDef.Path) + "]" : "";
            commentList.Add(SyntaxFactory.Comment($"/// >> {typeDef.Id} - {typeDef.TypeDef}{path}"));
         }

         if (typeName != null)
         {
            commentList.Add(SyntaxFactory.Comment($"/// >> {typeName}"));
         }

         if (docs != null)
         {
            foreach (string doc in docs)
            {
               string[] lines = doc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

               foreach (string line in lines)
               {
                  commentList.Add(SyntaxFactory.Comment($"/// {line}"));
               }
            }
         }

         commentList.Add(SyntaxFactory.Comment("/// </summary>"));

         return SyntaxFactory.TriviaList(commentList);
      }

      public static SyntaxTrivia HeaderComment => SyntaxFactory.Comment(
@"//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------");

      public static CompilationUnitSyntax CreateEnumType()
      {
         NamespaceDeclarationSyntax enumNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("YourNamespaceName"))
            .AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")),
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Substrate.NetApi.Model.Types"))
            );

         ClassDeclarationSyntax classDeclaration = SyntaxFactory.ClassDeclaration("EnumType")
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName("BaseType"))
            );

         var properties = new List<PropertyDeclarationSyntax>
         {
            SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName("Enum"), "Value")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                ),

            SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName("Substrate.NetApi.Model.Types.IType"), "Value2")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                )
         };

         MethodDeclarationSyntax fromBaseEnumMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("EnumType"), "FromBaseEnum")
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword))
            .AddParameterListParameters(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("t"))
                    .WithType(SyntaxFactory.ParseTypeName("Substrate.NetApi.Model.Types.Base.BaseEnumType"))
            )
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ReturnStatement(
                    SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("EnumType"))
                        .WithInitializer(
                            SyntaxFactory.InitializerExpression(SyntaxKind.ObjectInitializerExpression)
                                .AddExpressions(
                                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName("Value"),
                                        SyntaxFactory.InvocationExpression(
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.IdentifierName("t"),
                                                SyntaxFactory.IdentifierName("GetValue")
                                            )
                                        )
                                    ),
                                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                        SyntaxFactory.IdentifierName("Value2"),
                                        SyntaxFactory.InvocationExpression(
                                            SyntaxFactory.MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                SyntaxFactory.IdentifierName("t"),
                                                SyntaxFactory.IdentifierName("GetValue2")
                                            )
                                        )
                                    )
                                )
                        )
                )
            ));

         MethodDeclarationSyntax encodeMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("byte[]"), "Encode")
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName("List<byte>"))
                        .AddVariables(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("bytes"))
                                .WithInitializer(
                                    SyntaxFactory.EqualsValueClause(
                                        SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("List<byte>"))
                                    )
                                )
                        )
                ),
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("bytes"),
                            SyntaxFactory.IdentifierName("Add")
                        )
                    )
                ),
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("bytes"),
                            SyntaxFactory.IdentifierName("AddRange")
                        )
                    )
                ),
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("Bytes"),
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("bytes"),
                                SyntaxFactory.IdentifierName("ToArray")
                            )
                        )
                    )
                ),
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("TypeSize"),
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("Bytes"),
                            SyntaxFactory.IdentifierName("Length")
                        )
                    )
                ),
                SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("Bytes"))
            ));

         // Create the Decode method
         MethodDeclarationSyntax decodeMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "Decode")
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
             .AddParameterListParameters(
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("byteArray"))
                     .WithType(SyntaxFactory.ArrayType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)))
                         .WithRankSpecifiers(SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                             SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())
                         )))
                     ),
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("p"))
                     .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword)))
                     .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)))
             )
             .WithBody(SyntaxFactory.Block(
                 SyntaxFactory.ThrowStatement(SyntaxFactory.ObjectCreationExpression(
                     SyntaxFactory.ParseTypeName("InvalidOperationException"))
                 )
             ));


         classDeclaration = classDeclaration.AddMembers(
            fromBaseEnumMethod,
            encodeMethod,
            decodeMethod
        );

         // Add properties to the class declaration
         classDeclaration = classDeclaration.AddMembers(properties.ToArray());

         // Create the compilation unit
         CompilationUnitSyntax compilationUnit = SyntaxFactory.CompilationUnit()
             .AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")))
             .AddMembers(enumNamespace.AddMembers(classDeclaration));

         CompilationUnitSyntax enumTargetUnit = SyntaxFactory.CompilationUnit();
         enumTargetUnit = enumTargetUnit.AddMembers(compilationUnit.Members.ToArray());

         return enumTargetUnit;
      }
   }
}