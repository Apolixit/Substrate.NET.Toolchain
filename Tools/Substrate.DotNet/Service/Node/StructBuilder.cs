using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Substrate.DotNet.Client.Versions;
using Substrate.DotNet.Extensions;
using Substrate.DotNet.Service.Node.Base;
using Substrate.NetApi.Model.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using static Substrate.DotNet.Client.Versions.NodeTypeRefined;
using static Substrate.DotNet.Service.Node.ModuleGenBuilder;

namespace Substrate.DotNet.Service.Node
{
   public class StructBuilder : TypeBuilderBase
   {
      private NodeTypeResolved? _motherClass { get; set; }
      private LevelTypeNode _levelTypeNode { get; set; }
      private List<NodeTypeRefined> _nodeTypes { get; set; } = new List<NodeTypeRefined>();

      private StructBuilder(string projectName, uint id, NodeTypeComposite typeDef, NodeTypeResolver typeDict, NodeTypeResolved? motherClass, LevelTypeNode levelTypeNode, List<NodeTypeRefined> nodeTypes)
          : base(projectName, id, typeDef, typeDict)
      {
         _motherClass = motherClass;
         _levelTypeNode = levelTypeNode;
         _nodeTypes = nodeTypes;
      }

      private static FieldDeclarationSyntax GetPropertyFieldRoslyn(string name, string baseType)
      {
         FieldDeclarationSyntax fieldDeclaration = SyntaxFactory.FieldDeclaration(
             SyntaxFactory.VariableDeclaration(
                 SyntaxFactory.ParseTypeName(baseType),
                 SyntaxFactory.SingletonSeparatedList(
                     SyntaxFactory.VariableDeclarator(name.MakePrivateField()))))
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword));

         return fieldDeclaration;
      }

      private static PropertyDeclarationSyntax GetPropertyWithFieldRoslyn(string name, FieldDeclarationSyntax propertyField)
      {
         string propertyName = name.MakeMethod();
         TypeSyntax propertyType = propertyField.Declaration.Type;

         PropertyDeclarationSyntax prop = SyntaxFactory.PropertyDeclaration(propertyType, propertyName)
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddAccessorListAccessors(
                 SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                     .WithBody(SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(propertyField.Declaration.Variables[0].Identifier)))),
                 SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                     .WithBody(SyntaxFactory.Block(
                         SyntaxFactory.ExpressionStatement(
                             SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                 SyntaxFactory.IdentifierName(propertyField.Declaration.Variables[0].Identifier),
                                 SyntaxFactory.IdentifierName("value"))))));

         return prop;
      }

      private static PropertyDeclarationSyntax GetPropertyRoslyn(string name, TypeSyntax type)
      {
         string propertyName = name.MakeMethod();

         PropertyDeclarationSyntax prop = SyntaxFactory.PropertyDeclaration(type, propertyName)
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddAccessorListAccessors(
                 SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                     .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                 SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                     .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

         return prop;
      }

      private MethodDeclarationSyntax GetTypeByVersionRoslyn(NodeTypeComposite typeDef)
      {
         IEnumerable<NodeTypeRefined> linkedToVersion = _nodeTypes.Where(x => x is NodeTypeRefinedChild child && child.LinkedTo is not null && child.LinkedTo.NodeResolved.NodeType.Id == typeDef.Id);

         TypeSyntax motherTypeSyntax = SyntaxFactory.ParseTypeName(BuilderBase.GetMotherTypeDeclaration(GetFullItemPath(typeDef.Id)));

         MethodDeclarationSyntax createMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName($"System.Type"), "TypeByVersion")
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword))
             .AddParameterListParameters(
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")));

         var statements = new List<StatementSyntax>();

         foreach (NodeTypeRefined nodeTypeRefined in linkedToVersion)
         {
            buildTypeByVersion(statements, nodeTypeRefined);
         }

         buildTypeByVersion(statements, linkedToVersion.Last(), checkVersion: false);

         createMethod = createMethod.WithBody(SyntaxFactory.Block(statements));

         return createMethod;
      }

      private void buildTypeByVersion(List<StatementSyntax> statements, NodeTypeRefined nodeTypeRefined, bool checkVersion = true)
      {
         string item = GetFullItemPath(nodeTypeRefined.NodeResolved.NodeType.Id).ToString();
         ReturnStatementSyntax returnType = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName($"typeof({item})"));

         if(checkVersion)
         {
            statements.Add(SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
         SyntaxFactory.IdentifierName("version"),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(nodeTypeRefined.ToNumberVersion()))
                 ),
                SyntaxFactory.Block(
                   returnType
               )
            ));
         } else
         {
            statements.Add(returnType);
         }
      }

      private MethodDeclarationSyntax GetCreateByVersionRoslyn(NodeTypeComposite typeDef)
      {
         IEnumerable<NodeTypeRefined> linkedToVersion = _nodeTypes.Where(x => x is NodeTypeRefinedChild child && child.LinkedTo is not null && child.LinkedTo.NodeResolved.NodeType.Id == typeDef.Id);

         TypeSyntax motherTypeSyntax = SyntaxFactory.ParseTypeName(BuilderBase.GetMotherTypeDeclaration(GetFullItemPath(typeDef.Id)));

         MethodDeclarationSyntax createMethod = SyntaxFactory.MethodDeclaration(motherTypeSyntax, "Create")
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword))
             .AddParameterListParameters(
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("data")).WithType(SyntaxFactory.ParseTypeName("byte[]")),
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")));

         VariableDeclarationSyntax paramResultVariable =
             SyntaxFactory.VariableDeclaration(motherTypeSyntax)
             .AddVariables(
                 SyntaxFactory.VariableDeclarator("instance")
                 .WithInitializer(
                     SyntaxFactory.EqualsValueClause(
                         SyntaxFactory.LiteralExpression(
                             SyntaxKind.NullLiteralExpression))));

         var statements = new List<StatementSyntax>
         {
            SyntaxFactory.LocalDeclarationStatement(paramResultVariable)
         };

         foreach (NodeTypeRefined nodeTypeRefined in linkedToVersion)
         {
            buildCreateByVersion(statements, nodeTypeRefined);
         }

         buildCreateByVersion(statements, linkedToVersion.Last(), checkVersion: false);

         createMethod = createMethod.WithBody(SyntaxFactory.Block(statements));

         return createMethod;
      }

      private void buildCreateByVersion(List<StatementSyntax> statements, NodeTypeRefined nodeTypeRefined, bool checkVersion = true)
      {
         StatementSyntax newInstance = SyntaxFactory.ParseStatement($"instance = new {GetFullItemPath(nodeTypeRefined.NodeResolved.NodeType.Id)}();");

         ExpressionStatementSyntax callMethod = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
               SyntaxFactory.MemberAccessExpression(
                  SyntaxKind.SimpleMemberAccessExpression,
                  SyntaxFactory.IdentifierName("instance"),
                  SyntaxFactory.IdentifierName("Create")
               ),
               SyntaxFactory.ArgumentList(
                  SyntaxFactory.SingletonSeparatedList(
                     SyntaxFactory.Argument(
                           SyntaxFactory.IdentifierName("data")
                     )
                  )
               )
            )
         );

         ReturnStatementSyntax returnType = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName($"instance"));

         if (checkVersion)
         {
            statements.Add(SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.EqualsExpression,
         SyntaxFactory.IdentifierName("version"),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(nodeTypeRefined.ToNumberVersion()))
                 ),
                SyntaxFactory.Block(
                   newInstance,
                   callMethod,
                   returnType
               )
            ));
         }
         else
         {
            statements.Add(newInstance);
            statements.Add(callMethod);
            statements.Add(returnType);
         }
      }

      private MethodDeclarationSyntax GetDecodeRoslyn(NodeTypeField[] typeFields)
      {
         MethodDeclarationSyntax decodeMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), "Decode")
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddParameterListParameters(
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("byteArray")).WithType(SyntaxFactory.ParseTypeName("byte[]")),
                 SyntaxFactory.Parameter(SyntaxFactory.Identifier("p")).WithType(SyntaxFactory.ParseTypeName("int")).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword))));

         decodeMethod = decodeMethod.AddModifiers(SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
          .WithBody(SyntaxFactory.Block());

         decodeMethod = decodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement("var start = p;"));

         if (typeFields != null)
         {
            for (int i = 0; i < typeFields.Length; i++)
            {
               NodeTypeField typeField = typeFields[i];

               string fieldName = GetFieldName(typeField, "value", typeFields.Length, i);
               NodeTypeResolved fullItem = GetFullItemPath(typeField.TypeId);

               decodeMethod = decodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement($"{fieldName.MakeMethod()} = new {fullItem}();"));
               decodeMethod = decodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement($"{fieldName.MakeMethod()}.Decode(byteArray, ref p);"));
            }
         }

         decodeMethod = decodeMethod.AddBodyStatements(
             SyntaxFactory.ParseStatement("var bytesLength = p - start;"),
             SyntaxFactory.ParseStatement("TypeSize = bytesLength;"),
             SyntaxFactory.ParseStatement("Bytes = new byte[bytesLength];"),
             SyntaxFactory.ParseStatement("System.Array.Copy(byteArray, start, Bytes, 0, bytesLength);")
         );

         return decodeMethod;
      }

      private MethodDeclarationSyntax GetEncodeRoslyn(NodeTypeField[] typeFields)
      {
         MethodDeclarationSyntax encodeMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("System.Byte[]"), "Encode")
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.OverrideKeyword))
             .WithBody(SyntaxFactory.Block());

         encodeMethod = encodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement("var result = new List<byte>();"));

         if (typeFields != null)
         {
            for (int i = 0; i < typeFields.Length; i++)
            {
               NodeTypeField typeField = typeFields[i];
               string fieldName = StructBuilder.GetFieldName(typeField, "value", typeFields.Length, i);

               encodeMethod = encodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement($"result.AddRange({fieldName.MakeMethod()}.Encode());"));
            }
         }

         encodeMethod = encodeMethod.AddBodyStatements(SyntaxFactory.ParseStatement("return result.ToArray();"));

         return encodeMethod;
      }

      public static BuilderBase Init(string projectName, uint id, NodeTypeComposite typeDef, NodeTypeResolver typeDict)
      {
         return new StructBuilder(projectName, id, typeDef, typeDict, null, LevelTypeNode.Child, new List<NodeTypeRefined>());
      }

      public static BuilderBase Init(string projectName, uint id, NodeTypeComposite typeDef, NodeTypeResolver typeDict, NodeTypeResolved motherClass, LevelTypeNode levelTypeNode, List<NodeTypeRefined> nodeTypes)
      {
         return new StructBuilder(projectName, id, typeDef, typeDict, motherClass, levelTypeNode, nodeTypes);
      }

      public override TypeBuilderBase Create()
      {
         var typeDef = TypeDef as NodeTypeComposite;

         ClassName = $"{typeDef.Path.Last()}";

         ReferenzName = $"{NamespaceName}.{ClassName}";

         string motherClassName = (_motherClass is not null) ? _motherClass.NodeType.Path.Last() : "BaseType";

         ClassDeclarationSyntax targetClass = SyntaxFactory.ClassDeclaration(ClassName)
             .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
             .AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName(motherClassName)));

         if (_levelTypeNode == LevelTypeNode.Child)
         {
            targetClass = targetClass.AddModifiers(SyntaxFactory.Token(SyntaxKind.SealedKeyword));
         }
         else
         {
            targetClass = targetClass.AddModifiers(SyntaxFactory.Token(SyntaxKind.AbstractKeyword));
         }

         targetClass = AddTargetClassCustomAttributesRoslyn(targetClass, typeDef);

         if (_motherClass is not null)
         {
            TargetUnit = TargetUnit.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(_motherClass.Namespace)));
         }

         // add comment to class if exists
         targetClass = targetClass.WithLeadingTrivia(GetCommentsRoslyn(typeDef.Docs, typeDef));

         if (typeDef.TypeFields != null && !(_levelTypeNode == LevelTypeNode.Child && _motherClass is not null))
         {
            for (int i = 0; i < typeDef.TypeFields.Length; i++)
            {
               NodeTypeField typeField = typeDef.TypeFields[i];
               string fieldName = GetFieldName(typeField, "value", typeDef.TypeFields.Length, i);

               NodeTypeResolved fullItem = GetFullItemPath(typeField.TypeId);
               PropertyDeclarationSyntax propertyDeclaration = GetPropertyDeclaration(fieldName, fullItem);
               propertyDeclaration = propertyDeclaration.WithLeadingTrivia(GetCommentsRoslyn(typeField.Docs, null, fieldName));

               targetClass = targetClass.AddMembers(propertyDeclaration);
            }
         }

         MethodDeclarationSyntax nameMethod = SimpleMethodRoslyn("TypeName", "System.String", ClassName);
         targetClass = targetClass.AddMembers(nameMethod);

         MethodDeclarationSyntax encodeMethod = GetEncodeRoslyn(typeDef.TypeFields);
         targetClass = targetClass.AddMembers(encodeMethod);

         /**
          * Decode method is not implemented in mother class (will be override in children classes)
          * While mother class declare a new static "Create" method which will call children create method depends of SpecVersion
          **/
         if (!IsMotherClass())
         {
            MethodDeclarationSyntax decodeMethod = GetDecodeRoslyn(typeDef.TypeFields);
            targetClass = targetClass.AddMembers(decodeMethod);
         }
         else
         {
            MethodDeclarationSyntax typeMethod = GetTypeByVersionRoslyn(typeDef);
            targetClass = targetClass.AddMembers(typeMethod);

            MethodDeclarationSyntax createMethod = GetCreateByVersionRoslyn(typeDef);
            targetClass = targetClass.AddMembers(createMethod);
         }

         NamespaceDeclarationSyntax namespaceDeclaration = SyntaxFactory
            .NamespaceDeclaration(SyntaxFactory.IdentifierName(NamespaceName))
             .AddMembers(targetClass);

         CompilationUnitSyntax compilationUnit = SyntaxFactory.CompilationUnit()
             .AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System")))
             .AddMembers(namespaceDeclaration);

         TargetUnit = TargetUnit.AddMembers(compilationUnit.Members.ToArray());

         return this;
      }

      private bool IsMotherClass()
      {
         return _levelTypeNode == LevelTypeNode.Mother;
      }

      /// <summary>
      /// Manage property classes in mother class
      /// </summary>
      /// <param name="fieldName"></param>
      /// <param name="fullItem"></param>
      /// <returns></returns>
      private PropertyDeclarationSyntax GetPropertyDeclaration(string fieldName, NodeTypeResolved fullItem)
      {
         if (IsMotherClass())
         {
            return GetPropertyRoslyn(fieldName, SyntaxFactory.ParseTypeName(BuilderBase.GetMotherTypeDeclaration(fullItem)));
         }

         return GetPropertyRoslyn(fieldName, SyntaxFactory.ParseTypeName(fullItem.ToString()));
      }

      public static string GetFieldName(NodeTypeField typeField, int length, int index) => GetFieldName(typeField, "value", length, index);
      public static string GetFieldName(NodeTypeField typeField, string alterName, int length, int index)
      {
         if (typeField.Name == null)
         {
            if (length > 1)
            {
               if (typeField.TypeName == null)
               {
                  return alterName + index;
               }
               else
               {
                  return typeField.TypeName;
               }
            }
            else
            {
               return alterName;
            }
         }
         else
         {
            return typeField.Name;
         }
      }

      public static string GetFieldNameMotherType(NodeTypeField typeField, string alterName, int length, int index)
      {
         if (typeField.Name == null)
         {
            return alterName;
         }
         else
         {
            return typeField.Name;
         }
      }
   }
}