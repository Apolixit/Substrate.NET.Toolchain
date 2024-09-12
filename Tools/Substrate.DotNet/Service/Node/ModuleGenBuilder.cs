using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;
using Substrate.DotNet.Client.Versions;
using Substrate.DotNet.Extensions;
using Substrate.DotNet.Service.Node.Base;
using Substrate.NetApi.Model.Extrinsics;
using Substrate.NetApi.Model.Meta;
using Substrate.ServiceLayer.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using static Substrate.DotNet.Service.Node.ModuleGenBuilder;

namespace Substrate.DotNet.Service.Node
{
   public class ModuleGenBuilder : ModuleBuilderBase
   {
      public enum TypeModule
      {
         /// <summary>
         /// Module with a given SpecVersion
         /// </summary>
         Version,
         /// <summary>
         /// Global module which call Version modules
         /// </summary>
         Aggregation,
      }

      public TypeModule ModuleType { get; set; }
      public IEnumerable<ModuleVersion> AssociatedModulesVersion { get; set; }
      public IDictionary<uint, uint> MappingMother { get; set; }

      private ModuleGenBuilder(string projectName, uint id, PalletModule module, NodeTypeResolver typeDict, Dictionary<uint, NodeType> nodeTypes, TypeModule moduleType, IEnumerable<ModuleVersion> associatedModulesVersion, IDictionary<uint, uint> mappingMother) :
          base(projectName, id, module, typeDict, nodeTypes)
      {
         ModuleType = moduleType;
         AssociatedModulesVersion = associatedModulesVersion;
         MappingMother = mappingMother;
      }

      public static ModuleGenBuilder Init(string projectName, uint id, PalletModule module, NodeTypeResolver typeDict, Dictionary<uint, NodeType> nodeTypes, TypeModule moduleType, IEnumerable<ModuleVersion> associatedModulesVersion, IDictionary<uint, uint> mappingMother)
      {
         return new ModuleGenBuilder(projectName, id, module, typeDict, nodeTypes, moduleType, associatedModulesVersion, mappingMother);
      }

      public override ModuleGenBuilder Create()
      {
         UsingDirectiveSyntax[] usings = new[]
         {
            "System.Threading.Tasks",
            "Substrate.NetApi.Model.Meta",
            "System.Threading",
            "Substrate.NetApi",
            "Substrate.NetApi.Model.Types",
            "Substrate.NetApi.Model.Extrinsics",
         }.Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u))).ToArray();

         FileName = "Main" + Module.Name;
         NamespaceName = $"{ProjectName}.Generated.Storage";
         if (!string.IsNullOrEmpty(ProjectSpecVersion))
         {
            NamespaceName += $".{ProjectSpecVersion}";
         }
         ReferenzName = NamespaceName;

         NamespaceDeclarationSyntax typeNamespace = SyntaxFactory
            .NamespaceDeclaration(SyntaxFactory.ParseName(NamespaceName));

         typeNamespace = CreateStorage(typeNamespace);
         //typeNamespace = CreateCalls(typeNamespace);
         typeNamespace = CreateEvents(typeNamespace);
         typeNamespace = CreateConstants(typeNamespace);
         typeNamespace = CreateErrors(typeNamespace);

         TargetUnit = TargetUnit
            .AddUsings(usings)
            .AddMembers(typeNamespace);

         return this;
      }

      private NamespaceDeclarationSyntax CreateStorage(NamespaceDeclarationSyntax typeNamespace)
      {
         ClassName = Module.Name + "Storage";

         PalletStorage storage = Module.Storage;

         ConstructorDeclarationSyntax constructor = SyntaxFactory
            .ConstructorDeclaration(ClassName)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .WithBody(SyntaxFactory.Block());

         // Create the class
         ClassDeclarationSyntax targetClass = SyntaxFactory.ClassDeclaration(ClassName)
              .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)));

         // Create the client field.
         FieldDeclarationSyntax clientField = SyntaxFactory.FieldDeclaration(
                 SyntaxFactory.VariableDeclaration(
                     SyntaxFactory.ParseTypeName("SubstrateClientExt"))
                 .WithVariables(
                     SyntaxFactory.SingletonSeparatedList(
                         SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("_client")))))
             .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
             .WithLeadingTrivia(GetCommentsRoslyn(new string[] { "Substrate client for the storage calls." }));
         targetClass = targetClass.AddMembers(clientField);

         PropertyDeclarationSyntax blockHashProperty = CreateBlockHashPropery();
         targetClass = targetClass.AddMembers(blockHashProperty);

         // Add parameters.
         constructor = constructor.AddParameterListParameters(
             SyntaxFactory.Parameter(SyntaxFactory.Identifier("client"))
                 .WithType(SyntaxFactory.ParseTypeName(clientField.Declaration.Type.ToString())));

         // Assignment statement for the constructor.
         constructor = constructor.AddBodyStatements(
             SyntaxFactory.ExpressionStatement(
                 SyntaxFactory.AssignmentExpression(
                     SyntaxKind.SimpleAssignmentExpression,
                     SyntaxFactory.IdentifierName("_client"),
                     SyntaxFactory.IdentifierName("client"))));

         if (ModuleType == TypeModule.Aggregation)
         {
            MethodDeclarationSyntax versionMethod = CreateVersionMethod();
            targetClass = targetClass.AddMembers(versionMethod);

            // For aggregation mode, we have ModuleStorage for every version in the constructor
            foreach (ModuleVersion associatedVersion in AssociatedModulesVersion)
            {
               string nameFullStorageVersion = VersionnedModuleStorageName(associatedVersion.Version);
               string nameVersionnedModule = VersionnedModuleName(associatedVersion.Version);

               FieldDeclarationSyntax versionnedModuleField = SyntaxFactory.FieldDeclaration(
                 SyntaxFactory.VariableDeclaration(
                     SyntaxFactory.ParseTypeName(nameFullStorageVersion))
                 .WithVariables(
                     SyntaxFactory.SingletonSeparatedList(
                         SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(nameVersionnedModule)))))
             .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword)))
             .WithLeadingTrivia(GetCommentsRoslyn(new string[] { $"Storage for SpecVersion {associatedVersion.Version}" }));
               targetClass = targetClass.AddMembers(versionnedModuleField);

               constructor = constructor.AddBodyStatements(SyntaxFactory.ExpressionStatement(
                  SyntaxFactory.AssignmentExpression(
                     SyntaxKind.SimpleAssignmentExpression,
                     SyntaxFactory.IdentifierName(nameVersionnedModule),
                     SyntaxFactory.IdentifierName($"new {nameFullStorageVersion}(_client)")
                  )
               ));
            }
         }

         if (storage?.Entries != null)
         {
            foreach (Entry entry in storage.Entries)
            {
               string storageParams = entry.Name + "Params";

               // Create static method
               MethodDeclarationSyntax parameterMethod = SyntaxFactory
                  .MethodDeclaration(SyntaxFactory.ParseTypeName("string"), storageParams)
                  .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                  .WithLeadingTrivia(GetCommentsRoslyn(entry.Docs, null, storageParams)); // Assuming GetComments() returns a string

               MethodDeclarationSyntax storageMethod;
               ExpressionStatementSyntax methodInvoke;
               string returnValueStr;

               string methodName = ModuleType == TypeModule.Version ? entry.Name : entry.Name + "Async";

               if (entry.StorageType == Storage.Type.Plain)
               {
                  returnValueStr = (ModuleType == TypeModule.Aggregation) ? BuilderBase.GetMotherTypeDeclaration(GetFullItemPath(entry.TypeMap.Item1)) : GetFullItemPath(entry.TypeMap.Item1).ToString();

                  bool returnCompatible = IsReturnTypeCompatible(entry);
                  returnValueStr = returnCompatible ? returnValueStr : "IType";

                  storageMethod = SyntaxFactory
                  .MethodDeclaration(SyntaxFactory.ParseTypeName($"Task<{returnValueStr}>"), methodName);

                  parameterMethod = CreateParameterMethod(parameterMethod, entry, storage);

                  methodInvoke = SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(parameterMethod.Identifier)));

                  // add storage key mapping in constructor
                  //constructor = constructor
                  //   .AddBodyStatements(AddPropertyValuesRoslyn(GetStorageMapStringRoslyn("", returnValueStr.ToString(), storage.Prefix, entry.Name), "_client.StorageKeyDict"));
               }
               else if (entry.StorageType == Storage.Type.Map)
               {
                  TypeMap typeMap = entry.TypeMap.Item2;
                  Storage.Hasher[] hashers = typeMap.Hashers;
                  NodeTypeResolved key = GetFullItemPath(typeMap.Key);
                  string keyValueStr = (ModuleType == TypeModule.Aggregation) ? BuilderBase.GetMotherTypeDeclaration(key) : key.ToString();
                  bool keyCompatible = IsKeyTypeCompatible(entry);
                  keyValueStr = keyCompatible ? keyValueStr : "IType";

                  returnValueStr = (ModuleType == TypeModule.Aggregation) ? BuilderBase.GetMotherTypeDeclaration(GetFullItemPath(typeMap.Value)) : GetFullItemPath(typeMap.Value).ToString();
                  bool returnCompatible = IsReturnTypeCompatible(entry);
                  returnValueStr = returnCompatible ? returnValueStr : "IType";

                  storageMethod = SyntaxFactory
                     .MethodDeclaration(SyntaxFactory.ParseTypeName($"Task<{returnValueStr}>"), methodName);

                  parameterMethod = parameterMethod
                     .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("key"))
                     .WithType(SyntaxFactory.ParseTypeName(keyValueStr)));
                  parameterMethod = CreateParameterMethod(parameterMethod, entry, storage, hashers);

                  storageMethod = storageMethod
                     .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("key"))
                     .WithType(SyntaxFactory.ParseTypeName(keyValueStr)));

                  ArgumentListSyntax argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Argument(SyntaxFactory.IdentifierName("key")) }));

                  methodInvoke = SyntaxFactory.ExpressionStatement(
                     SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName(parameterMethod.Identifier), argumentList));

                  // add storage key mapping in constructor
                  //constructor = constructor.AddBodyStatements(AddPropertyValuesRoslyn(GetStorageMapStringRoslyn(keyValueStr, returnValueStr.ToString(), storage.Prefix, entry.Name, hashers), "_client.StorageKeyDict"));
               }
               else
               {
                  throw new NotImplementedException();
               }

               storageMethod = CreateStorageMethod(storageMethod, entry, methodInvoke, returnValueStr);

               // add parameter method to the class
               targetClass = targetClass.AddMembers(parameterMethod);

               // default function
               if (entry.Default != null && entry.Default.Length != 0)
               {
                  string storageDefault = entry.Name + "Default";
                  MethodDeclarationSyntax defaultMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("string"), storageDefault)
                      .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                      .WithLeadingTrivia(GetCommentsRoslyn(new string[] { "Default value as hex string" }, null, storageDefault));

                  // add default method to the class
                  targetClass = targetClass.AddMembers(CreateDefaultMethod(defaultMethod, entry));
               }

               // add storage method to the class
               targetClass = targetClass.AddMembers(storageMethod);

               // Add a InputType method if we are a module aggregation and have key input
               // This is use to return the conrete key type regards of the version
               if (ModuleType == TypeModule.Aggregation && entry.StorageType == Storage.Type.Map)
               {
                  MethodDeclarationSyntax typeMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName($"System.Type"), entry.Name + "InputType");
                  typeMethod = CreateTypeMethod(typeMethod, entry);
                  targetClass = targetClass.AddMembers(typeMethod);
               }
            }
         }

         // add constructor to the class
         targetClass = targetClass.AddMembers(constructor);

         // Add class to the namespace.
         typeNamespace = typeNamespace.AddMembers(targetClass);

         return typeNamespace;
      }

      private string VersionnedModuleStorageName(uint version)
      {
         return $"{NamespaceName}.v{version}.{Module.Name}Storage";
      }

      private string VersionnedModuleConstanteName(uint version)
      {
         return $"{NamespaceName}.v{version}.{Module.Name}Constants";
      }

      private string VersionnedModuleName(uint version)
      {
         return $"_{Module.Name.ToLowerFirst()}StorageV{version}";
      }

      #region Custom aggregation behavior
      private static PropertyDeclarationSyntax CreateBlockHashPropery()
      {
         // SyntaxFactory.NullableType(SyntaxFactory.ParseTypeName("string")), "blockHash")
         return SyntaxFactory.PropertyDeclaration(
                        SyntaxFactory.ParseTypeName("string"), "blockHash")
                        .WithModifiers(
                           SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                        )
                        .WithAccessorList(
                           SyntaxFactory.AccessorList(
                              SyntaxFactory.List(
                                 new[]
                                {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                                }
                           ))
                        )
                        .WithInitializer(
                           SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))
                        ).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
      }

      private static MethodDeclarationSyntax CreateVersionMethod()
      {
         MethodDeclarationSyntax versionMethod = SyntaxFactory
                                .MethodDeclaration(SyntaxFactory.ParseTypeName("Task<uint>"), "GetVersionAsync")
                                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AsyncKeyword)))
                                .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("token")).WithType(SyntaxFactory.ParseTypeName("CancellationToken")));


         string resultString = "await _client.State.GetRuntimeVersionAsync(blockHash, token)";

         VariableDeclarationSyntax variableDeclaration = SyntaxFactory
            .VariableDeclaration(SyntaxFactory.IdentifierName("var"))
            .AddVariables(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("result"), null, SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(resultString))));

         versionMethod = versionMethod.AddBodyStatements(SyntaxFactory.LocalDeclarationStatement(variableDeclaration));

         versionMethod = versionMethod.AddBodyStatements(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("result.SpecVersion")));
         return versionMethod;
      }
      #endregion

      #region Default method
      private MethodDeclarationSyntax CreateDefaultMethod(MethodDeclarationSyntax defaultMethod, Entry entry)
      {
         return ModuleType == TypeModule.Version ?
                              CreateDefaultVersionMethod(defaultMethod, entry) :
                              CreateDefaultAggregationMethod(defaultMethod, entry);
      }

      private MethodDeclarationSyntax CreateDefaultVersionMethod(MethodDeclarationSyntax defaultMethod, Entry entry)
      {
         // Add return statement
         defaultMethod = defaultMethod.WithBody(SyntaxFactory.Block(SyntaxFactory.ReturnStatement(
             SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal("0x" + BitConverter.ToString(entry.Default).Replace("-", string.Empty))))));

         return defaultMethod;
      }

      private MethodDeclarationSyntax CreateDefaultAggregationMethod(MethodDeclarationSyntax defaultMethod, Entry entry)
      {
         return defaultMethod.AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")))
            .WithBody(GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, TypeMethod.Default));
      }
      #endregion

      #region TypeByVersion method
      private MethodDeclarationSyntax CreateTypeMethod(MethodDeclarationSyntax typeMethod, Entry entry)
      {
         typeMethod = typeMethod
                  .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

         typeMethod = typeMethod
            .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")));

         return typeMethod.WithBody(GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, TypeMethod.InputType));
      }

      private MethodDeclarationSyntax CreateTypeAggregationMethod(MethodDeclarationSyntax storageMethod, Entry entry)
      {
         return storageMethod.WithBody(GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, TypeMethod.Storage));
         //return storageMethod.WithBody(GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, returnValueStr, TypeMethod.Storage));
      }
      #endregion
      #region Storage method
      private MethodDeclarationSyntax CreateStorageMethod(MethodDeclarationSyntax storageMethod, Entry entry, ExpressionStatementSyntax methodInvoke, string returnValue)
      {
         storageMethod = storageMethod
                  .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.AsyncKeyword)))
                  .WithLeadingTrivia(GetCommentsRoslyn(entry.Docs, null, entry.Name)); // Assuming GetComments() returns a string

         storageMethod = storageMethod
            .AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("token")).WithType(SyntaxFactory.ParseTypeName("CancellationToken")));

         return ModuleType == TypeModule.Version ?
            CreateStorageVersionMethod(storageMethod, methodInvoke, returnValue.ToString()) :
            CreateStorageAggregationMethod(storageMethod, entry);
      }

      private MethodDeclarationSyntax CreateStorageVersionMethod(MethodDeclarationSyntax storageMethod, ExpressionStatementSyntax methodInvoke, string returnValueStr)
      {
         VariableDeclarationSyntax variableDeclaration1 = SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName("string"))
             .AddVariables(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("parameters"), null, SyntaxFactory.EqualsValueClause(methodInvoke.Expression)));

         storageMethod = storageMethod.AddBodyStatements(SyntaxFactory.LocalDeclarationStatement(variableDeclaration1));

         string resultString = GetInvoceString(returnValueStr.ToString());

         VariableDeclarationSyntax variableDeclaration2 = SyntaxFactory
            .VariableDeclaration(SyntaxFactory.IdentifierName("var"))
            .AddVariables(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("result"), null, SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(resultString))));

         storageMethod = storageMethod.AddBodyStatements(SyntaxFactory.LocalDeclarationStatement(variableDeclaration2));

         storageMethod = storageMethod.AddBodyStatements(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("result")));

         return storageMethod;
      }

      private MethodDeclarationSyntax CreateStorageAggregationMethod(MethodDeclarationSyntax storageMethod, Entry entry)
      {
         return storageMethod.WithBody(GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, TypeMethod.Storage));
      }
      #endregion

      #region Parameter method
      private MethodDeclarationSyntax CreateParameterMethod(
         MethodDeclarationSyntax parameterMethod,
         Entry entry, PalletStorage storage, Storage.Hasher[] hashers = null)
      {
         return ModuleType == TypeModule.Version ?
                              CreateParameterVersionMethod(parameterMethod, entry, storage, hashers) :
                              CreateParameterAggregationMethod(parameterMethod, entry);
      }

      private MethodDeclarationSyntax CreateParameterVersionMethod(MethodDeclarationSyntax parameterMethod, Entry entry, PalletStorage storage, Storage.Hasher[] hashers = null)
      {
         return parameterMethod.AddBodyStatements(
                     SyntaxFactory.ReturnStatement(GetStorageStringRoslyn(storage.Prefix, entry.Name, entry.StorageType, hashers)));
      }

      private MethodDeclarationSyntax CreateParameterAggregationMethod(MethodDeclarationSyntax parameterMethod, Entry entry)
      {
         return parameterMethod.AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")))
            .WithBody(
                     GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, TypeMethod.Parameter));
         //GetStorageStringRoslynAggregation(entry, AssociatedModulesVersion, "string", TypeMethod.Parameter));
      }
      #endregion

      public enum TypeMethod
      {
         Default,
         Parameter,
         Storage,
         InputType
      }

      private BlockSyntax GetStorageStringRoslynAggregation(
            Entry entry,
            IEnumerable<ModuleVersion> versions,
            TypeMethod typeMethod)
      {
         var statements = new List<StatementSyntax>();

         if (typeMethod == TypeMethod.Storage)
         {
            string resultVersion = "await GetVersionAsync(token)";

            VariableDeclarationSyntax versionDeclaration = SyntaxFactory
               .VariableDeclaration(SyntaxFactory.IdentifierName("var"))
               .AddVariables(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier("version"), null, SyntaxFactory.EqualsValueClause(SyntaxFactory.IdentifierName(resultVersion))));
            statements.Add(SyntaxFactory.LocalDeclarationStatement(versionDeclaration));
         }

         string typeMethodName = string.Empty;
         if (typeMethod == TypeMethod.Parameter)
         {
            typeMethodName = "Params";
         }
         else if (typeMethod == TypeMethod.Default)
         {
            typeMethodName = "Default";
         }
         else if (typeMethod == TypeMethod.InputType)
         {
            typeMethodName = "Type";
         }

         var parameters = new List<string>();
         string keyParam = "key";
         if (entry.StorageType == Storage.Type.Map && typeMethod != TypeMethod.Default && typeMethod != TypeMethod.InputType)
         {
            parameters.Add(keyParam);
         }
         if (typeMethod == TypeMethod.Storage)
         {
            parameters.Add("token");
         }

         foreach (ModuleVersion version in versions)
         {
            BuildStorageVersion(entry, typeMethod, statements, typeMethodName, parameters, keyParam, version);
         }

         ModuleVersion lastExistingVersion = versions.Where(v => v.Module.Storage?.Entries?.SingleOrDefault(x => x.Name == entry.Name) is not null).Last();
         BuildStorageVersion(entry, typeMethod, statements, typeMethodName, parameters, keyParam, lastExistingVersion, false);


         // Romain 10/09/2024 => Remove this throw statement and keep return the last version.
         // This is not really optimal but I want to avoid to throw an exception when a new version is released.

         //ThrowStatementSyntax throwStatement = SyntaxFactory.ThrowStatement(
         //        SyntaxFactory.ObjectCreationExpression(
         //            SyntaxFactory.QualifiedName(
         //                SyntaxFactory.ParseName("System"),
         //                SyntaxFactory.IdentifierName("InvalidOperationException")
         //            ), SyntaxFactory.ArgumentList(
         //        SyntaxFactory.SingletonSeparatedList(
         //            SyntaxFactory.Argument(
         //                SyntaxFactory.LiteralExpression(
         //                    SyntaxKind.StringLiteralExpression,
         //                    SyntaxFactory.Literal("Error while fetching data. The version is not supported, please check that a new version has not been release")
         //                )
         //            )
         //        )
         //    ), null));

         //statements.Add(throwStatement);
         return SyntaxFactory.Block(
             statements
         );
      }

      private void BuildStorageVersion(Entry entry, TypeMethod typeMethod, List<StatementSyntax> statements, string typeMethodName, List<string> parameters, string keyParam, ModuleVersion version, bool checkVersion = true)
      {
         Entry childEntry = version.Module.Storage?.Entries?.SingleOrDefault(x => x.Name == entry.Name);
         if (childEntry is null)
         {
            return;
         }

         bool hasBeenCasted = false;
         // Check if we have to cast "key" param
         if (parameters.Any(x => x == keyParam))
         {
            NodeTypeResolved childModuleType = GetFullItemPath(childEntry.TypeMap.Item2.Key);

            string castType = $"({childModuleType})";
            parameters.Remove(keyParam);

            keyParam = castType + "key";
            parameters.Insert(0, keyParam);

            hasBeenCasted = true;
         }

         string paramMethod = $"{entry.Name}{typeMethodName}({(parameters.Any() ? string.Join(",", parameters) : string.Empty)})";

         string call;
         switch (typeMethod)
         {
            case TypeMethod.Default:
               call = $"{NamespaceName}.v{version.Version}.{Module.Name}Storage.{paramMethod}";
               break;
            case TypeMethod.InputType:
               call = $"typeof({GetFullItemPath(childEntry.TypeMap.Item2.Key)})";
               break;
            case TypeMethod.Parameter:
               call = $"{NamespaceName}.v{version.Version}.{Module.Name}Storage.{paramMethod}";
               break;
            default:
               call = $"await {VersionnedModuleName(version.Version)}.{paramMethod}";
               break;
         }

         ReturnStatementSyntax returnAffectation = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(call));


         ExpressionStatementSyntax blockHashAffectation = SyntaxFactory.ExpressionStatement(
                             SyntaxFactory.AssignmentExpression(
                                 SyntaxKind.SimpleAssignmentExpression,
                                 SyntaxFactory.MemberAccessExpression(
                                     SyntaxKind.SimpleMemberAccessExpression,
                                     SyntaxFactory.IdentifierName(VersionnedModuleName(version.Version)),
                                     SyntaxFactory.IdentifierName("blockHash")
                                 ),
                                 SyntaxFactory.IdentifierName("blockHash")
                             )
                         );

         if (checkVersion)
         {
            statements.Add(SyntaxFactory.IfStatement(
             SyntaxFactory.BinaryExpression(
                 SyntaxKind.EqualsExpression,
                 SyntaxFactory.IdentifierName("version"),
                 SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(version.Version))
              ),
             typeMethod == TypeMethod.Storage ?
               SyntaxFactory.Block(blockHashAffectation, returnAffectation) :
               returnAffectation
            ));
         }
         else
         {
            AddLastVersionComment(statements);
            if(typeMethod == TypeMethod.Storage)
            {
               statements.Add(blockHashAffectation);
            }

            statements.Add(returnAffectation);
         }

         if(hasBeenCasted)
         {
            parameters.RemoveAt(0);
            parameters.Insert(0, "key");
         }
      }

      private static void AddLastVersionComment(List<StatementSyntax> statements)
      {
         statements.Add(SyntaxFactory.ParseStatement("")
                        .WithLeadingTrivia(SyntaxFactory.TriviaList(
                     SyntaxFactory.LineFeed,
                     SyntaxFactory.LineFeed,
                     SyntaxFactory.Comment("// Return by default the last version."),
                     SyntaxFactory.LineFeed,
                     SyntaxFactory.Comment("//If the caller need to know dynamically which is the last version handled, please call Substrate.NetApi.Ext LastVersionHandle() method."))));
      }

      private bool IsReturnTypeCompatible(Entry entry)
      {
         if (entry.StorageType == Storage.Type.Plain)
         {
            return IsTypeCompatible(entry, (Entry childEntry) => childEntry.TypeMap.Item1);
         }
         else if (entry.StorageType == Storage.Type.Map)
         {
            return IsTypeCompatible(entry, (Entry childEntry) => childEntry.TypeMap.Item2.Value);
         }

         throw new NotImplementedException();
      }


      private bool IsKeyTypeCompatible(Entry entry)
         => IsTypeCompatible(entry, (Entry childEntry) => childEntry.TypeMap.Item2.Key);

      private bool IsTypeCompatible(Entry entry, Func<Entry, uint> typeCompare)
      {
         if (AssociatedModulesVersion is null || !AssociatedModulesVersion.Any())
         {
            return true;
         }

         //uint? valueReturn = null;
         (NetApi.Model.Types.Metadata.Base.TypeDefEnum?, string?) initialValue = default;
         bool compatible = AssociatedModulesVersion
            .Where(version => version.Module.Storage?.Entries?.SingleOrDefault(x => x.Name == entry.Name) is not null)
            .Select(version => (version.Module.Storage?.Entries?.SingleOrDefault(x => x.Name == entry.Name)))
            .All(childEntry =>
            {
               NodeTypeResolved childModuleType = GetFullItemPath(typeCompare(childEntry));
               return IsCommonType(ref initialValue, childModuleType);
            });
         return compatible;
      }

      private bool IsCommonType(ref (NetApi.Model.Types.Metadata.Base.TypeDefEnum? typeDef, string? id) initialValue, NodeTypeResolved childModuleType)
      {
         if (childModuleType.NodeType.TypeDef == NetApi.Model.Types.Metadata.Base.TypeDefEnum.Composite)
         {
            uint id;
            _ = MappingMother.TryGetValue(childModuleType.NodeType.Id, out id);

            return Compare(ref initialValue, (NetApi.Model.Types.Metadata.Base.TypeDefEnum.Composite, id.ToString()));
         }
         else if (childModuleType.NodeType.TypeDef == NetApi.Model.Types.Metadata.Base.TypeDefEnum.Primitive)
         {
            return Compare(ref initialValue, (NetApi.Model.Types.Metadata.Base.TypeDefEnum.Primitive, childModuleType.ClassName));
         }
         else
         {
            return Compare(ref initialValue, (childModuleType.NodeType.TypeDef, null));
         }

         //uint value;
         //bool found = MappingMother.TryGetValue(childModuleType.NodeType.Id, out value);
         //if (!found && valueReturn is null)
         //{
         //   return true;
         //}
         //else if (valueReturn == null)
         //{
         //   valueReturn = value;
         //}

         //return value == valueReturn;

         bool Compare(ref (NetApi.Model.Types.Metadata.Base.TypeDefEnum? typeDef, string? id) initialValue, (NetApi.Model.Types.Metadata.Base.TypeDefEnum typeDef, string? id) currentValue)
         {
            if (initialValue.typeDef is null)
            {
               initialValue.typeDef = currentValue.typeDef;
               initialValue.id = currentValue.id;
               return true;
            }
            else
            {
               return initialValue.typeDef.Value == currentValue.typeDef && initialValue.id == currentValue.id;
            }
         }
      }

      private bool IsConstanteTypeCompatible(string constantName)
      {
         if (AssociatedModulesVersion is null || !AssociatedModulesVersion.Any())
         {
            return true;
         }

         //uint? valueReturn = null;
         (NetApi.Model.Types.Metadata.Base.TypeDefEnum?, string?) initialValue = default;
         bool compatible = AssociatedModulesVersion
            .Where(version => version.Module.Constants is not null)
            .Select(version => version.Module.Constants)
            .All(childConstant =>
            {
               PalletConstant constFound = childConstant.SingleOrDefault(x => x.Name == constantName);
               if (constFound is null)
               {
                  return true;
               }

               NodeTypeResolved childModuleType = GetFullItemPath(constFound.TypeId);
               return IsCommonType(ref initialValue, childModuleType);
            });
         return compatible;
      }

      private NamespaceDeclarationSyntax CreateCalls(NamespaceDeclarationSyntax namespaceDeclaration)
      {
         ClassName = Module.Name + "Calls";

         PalletCalls calls = Module.Calls;

         ClassDeclarationSyntax targetClass = SyntaxFactory.ClassDeclaration(ClassName)
             .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)));

         if (calls != null)
         {
            if (NodeTypes.TryGetValue(calls.TypeId, out NodeType nodeType))
            {
               var typeDef = nodeType as NodeTypeVariant;

               if (typeDef.Variants != null)
               {
                  foreach (TypeVariant variant in typeDef.Variants)
                  {
                     MethodDeclarationSyntax callMethod = SyntaxFactory
                        .MethodDeclaration(SyntaxFactory.ParseTypeName(nameof(Method)), variant.Name.MakeMethod())
                        .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                        .WithBody(SyntaxFactory.Block());

                     // add comment to class if exists
                     callMethod = callMethod.WithLeadingTrivia(GetCommentsRoslyn(typeDef.Docs, null, variant.Name));

                     string byteArrayName = "byteArray";

                     TypeSyntax byteListType = SyntaxFactory.ParseTypeName("List<byte>");

                     callMethod = callMethod.AddBodyStatements(
                         SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
                             byteListType,
                             SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(
                                 SyntaxFactory.Identifier(byteArrayName),
                                 null,
                                 SyntaxFactory.EqualsValueClause(SyntaxFactory.ObjectCreationExpression(byteListType)
                                     .WithArgumentList(SyntaxFactory.ArgumentList())))))));

                     if (variant.TypeFields != null)
                     {
                        foreach (NodeTypeField field in variant.TypeFields)
                        {
                           NodeTypeResolved fullItem = GetFullItemPath(field.TypeId);

                           // Adding '@' prefix to the parameter
                           string parameterName = EscapeIfKeyword(field.Name);

                           callMethod = callMethod.AddParameterListParameters(
                               SyntaxFactory.Parameter(SyntaxFactory.Identifier(parameterName))
                                   .WithType(SyntaxFactory.ParseTypeName(fullItem.ToString())));

                           callMethod = callMethod.AddBodyStatements(
                               SyntaxFactory.ExpressionStatement(
                                   SyntaxFactory.InvocationExpression(
                                       SyntaxFactory.MemberAccessExpression(
                                           SyntaxKind.SimpleMemberAccessExpression,
                                           SyntaxFactory.IdentifierName(byteArrayName),
                                           SyntaxFactory.IdentifierName("AddRange")),
                                       SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                           SyntaxFactory.Argument(
                                               SyntaxFactory.InvocationExpression(
                                                   SyntaxFactory.MemberAccessExpression(
                                                       SyntaxKind.SimpleMemberAccessExpression,
                                                       SyntaxFactory.IdentifierName(parameterName),
                                                       SyntaxFactory.IdentifierName("Encode")))))))));
                        }
                     }

                     // return statement
                     ObjectCreationExpressionSyntax create = SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(nameof(Method)))
                         .WithArgumentList(
                             SyntaxFactory.ArgumentList(
                                 SyntaxFactory.SeparatedList<ArgumentSyntax>(
                                     new SyntaxNodeOrToken[]
                                     {
                                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal((int)Module.Index))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(Module.Name))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(variant.Index))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(variant.Name))),
                                        SyntaxFactory.Token(SyntaxKind.CommaToken),
                                        SyntaxFactory.Argument(
                                            SyntaxFactory.InvocationExpression(
                                                SyntaxFactory.MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    SyntaxFactory.IdentifierName(byteArrayName),
                                                    SyntaxFactory.IdentifierName("ToArray")))),
                                     })));

                     ReturnStatementSyntax returnStatement = SyntaxFactory.ReturnStatement(create);

                     callMethod = callMethod.AddBodyStatements(returnStatement);
                     targetClass = targetClass.AddMembers(callMethod);
                  }
               }
            }
         }

         namespaceDeclaration = namespaceDeclaration.AddMembers(targetClass);
         return namespaceDeclaration;
      }

      private NamespaceDeclarationSyntax CreateEvents(NamespaceDeclarationSyntax namespaceDeclaration)
      {
         ClassName = Module.Name + "Events";

         PalletEvents events = Module.Events;

         //if (events != null && NodeTypes.TryGetValue(events.TypeId, out NodeType nodeType))
         //{
         //   var typeDef = nodeType as NodeTypeVariant;

         //   if (typeDef.Variants != null)
         //   {
         //      foreach (TypeVariant variant in typeDef.Variants)
         //      {
         //         string eventClassName = "Event" + variant.Name.MakeMethod();
         //         ClassDeclarationSyntax eventClass = SyntaxFactory.ClassDeclaration(eventClassName)
         //             .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
         //             .WithLeadingTrivia(GetCommentsRoslyn(variant.Docs, null, variant.Name));

         //         QualifiedNameSyntax baseTupleType = SyntaxFactory.QualifiedName(SyntaxFactory.IdentifierName("BaseTuple"), SyntaxFactory.IdentifierName(string.Empty));
         //         if (variant.TypeFields != null)
         //         {
         //            foreach (NodeTypeField field in variant.TypeFields)
         //            {
         //               NodeTypeResolved fullItem = GetFullItemPath(field.TypeId);
         //               baseTupleType = baseTupleType.WithRight(SyntaxFactory.IdentifierName(fullItem.ToString()));
         //            }
         //         }
         //         eventClass = eventClass.AddBaseListTypes(SyntaxFactory.SimpleBaseType(baseTupleType));

         //         namespaceDeclaration = namespaceDeclaration.AddMembers(eventClass);
         //      }
         //   }
         //}

         return namespaceDeclaration;
      }

      private NamespaceDeclarationSyntax CreateConstants(NamespaceDeclarationSyntax namespaceDeclaration)
      {
         ClassName = Module.Name + "Constants";

         PalletConstant[] constants = Module.Constants;

         ClassDeclarationSyntax targetClass = SyntaxFactory.ClassDeclaration(ClassName)
             .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)));

         if (constants != null && constants.Any())
         {
            foreach (PalletConstant constant in constants)
            {
               MethodDeclarationSyntax constantMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName("void"), constant.Name)
                   .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

               // add comment to class if exists
               constantMethod = constantMethod.WithLeadingTrivia(GetCommentsRoslyn(constant.Docs, null, constant.Name));

               targetClass = targetClass.AddMembers(constantMethod);

               if (NodeTypes.TryGetValue(constant.TypeId, out NodeType nodeType))
               {
                  NodeTypeResolved nodeTypeResolved = GetFullItemPath(nodeType.Id);
                  constantMethod = CreateConstantMethod(constantMethod, constant, nodeTypeResolved);

                  targetClass = targetClass.ReplaceNode(
                   targetClass.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == constant.Name),
                   constantMethod);
               }
            }
         }

         namespaceDeclaration = namespaceDeclaration.AddMembers(targetClass);
         return namespaceDeclaration;
      }

      private MethodDeclarationSyntax CreateConstantMethod(MethodDeclarationSyntax constantMethod, PalletConstant constant, NodeTypeResolved nodeTypeResolved)
      {
         return ModuleType == TypeModule.Version ?
                              CreateConstantVersionMethod(constantMethod, constant, nodeTypeResolved) :
                              CreateConstantAggregationMethod(constantMethod, constant, nodeTypeResolved);
      }

      private static MethodDeclarationSyntax CreateConstantVersionMethod(MethodDeclarationSyntax constantMethod, PalletConstant constant, NodeTypeResolved nodeTypeResolved)
      {
         constantMethod = constantMethod.WithReturnType(SyntaxFactory.ParseTypeName(nodeTypeResolved.ToString()));

         // assign new result object
         constantMethod = constantMethod.AddBodyStatements(
             SyntaxFactory.LocalDeclarationStatement(
                 SyntaxFactory.VariableDeclaration(
                     SyntaxFactory.IdentifierName("var"),
                     SyntaxFactory.SingletonSeparatedList(
                         SyntaxFactory.VariableDeclarator(
                             SyntaxFactory.Identifier("result"),
                             null,
                             SyntaxFactory.EqualsValueClause(
                                 SyntaxFactory.ObjectCreationExpression(
                                     SyntaxFactory.ParseTypeName(nodeTypeResolved.ToString()),
                                     SyntaxFactory.ArgumentList(),
                                     null)))))));

         // create with hex string object
         constantMethod = constantMethod.AddBodyStatements(
             SyntaxFactory.ExpressionStatement(
                 SyntaxFactory.InvocationExpression(
                     SyntaxFactory.MemberAccessExpression(
                         SyntaxKind.SimpleMemberAccessExpression,
                         SyntaxFactory.IdentifierName("result"),
                         SyntaxFactory.IdentifierName("Create")),
                     SyntaxFactory.ArgumentList(
                         SyntaxFactory.SingletonSeparatedList(
                             SyntaxFactory.Argument(
                                 SyntaxFactory.LiteralExpression(
                                     SyntaxKind.StringLiteralExpression,
                                     SyntaxFactory.Literal("0x" + BitConverter.ToString(constant.Value).Replace("-", string.Empty)))))))));

         // return statement
         constantMethod = constantMethod.AddBodyStatements(
             SyntaxFactory.ReturnStatement(
                 SyntaxFactory.IdentifierName("result")));

         return constantMethod;
      }

      private MethodDeclarationSyntax CreateConstantAggregationMethod(MethodDeclarationSyntax constantMethod, PalletConstant constant, NodeTypeResolved nodeTypeResolved)
      {
         bool isTypeCompatible = IsConstanteTypeCompatible(constant.Name);
         string returnTypeStr = isTypeCompatible ? BuilderBase.GetMotherTypeDeclaration(GetFullItemPath(nodeTypeResolved.NodeType.Id)) : "IType";

         constantMethod = constantMethod.WithReturnType(SyntaxFactory.ParseTypeName(returnTypeStr));

         constantMethod = constantMethod.AddParameterListParameters(SyntaxFactory.Parameter(SyntaxFactory.Identifier("version")).WithType(SyntaxFactory.ParseTypeName("uint")));

         string variableName = "result";
         var statements = new List<StatementSyntax>();

         VariableDeclarationSyntax resultVariable =
             SyntaxFactory.VariableDeclaration(
                 SyntaxFactory.ParseTypeName(returnTypeStr))
             .AddVariables(
                 SyntaxFactory.VariableDeclarator(variableName)
                 .WithInitializer(
                     SyntaxFactory.EqualsValueClause(
                         SyntaxFactory.LiteralExpression(
                             SyntaxKind.NullLiteralExpression))));
         statements.Add(SyntaxFactory.LocalDeclarationStatement(resultVariable));

         foreach (ModuleVersion version in AssociatedModulesVersion)
         {
            BuildConstantVersion(constant, variableName, statements, version);
         }

         BuildConstantVersion(constant, variableName, statements, AssociatedModulesVersion.Last());

         // Romain 10/09/2024 => Remove this throw statement and keep return the last version.
         // This is not really optimal but I want to avoid to throw an exception when a new version is released.

         //IfStatementSyntax ifStatement = SyntaxFactory.IfStatement(
         //    SyntaxFactory.BinaryExpression(
         //        SyntaxKind.EqualsExpression,
         //        SyntaxFactory.IdentifierName(variableName),
         //        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
         //    ),
         //    SyntaxFactory.ThrowStatement(
         //        SyntaxFactory.ObjectCreationExpression(
         //            SyntaxFactory.QualifiedName(
         //                SyntaxFactory.ParseName("System"),
         //                SyntaxFactory.IdentifierName("InvalidOperationException")
         //            ), SyntaxFactory.ArgumentList(
         //        SyntaxFactory.SingletonSeparatedList(
         //            SyntaxFactory.Argument(
         //                SyntaxFactory.LiteralExpression(
         //                    SyntaxKind.StringLiteralExpression,
         //                    SyntaxFactory.Literal("Error while fetching data. The version is not supported, please check that a new version has not been release")
         //                )
         //            )
         //        )
         //    ), null)
         //    )
         //);

         //statements.Add(ifStatement);
         statements.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(variableName)));
         return constantMethod.WithBody(SyntaxFactory.Block(
             statements
         ));
      }

      private void BuildConstantVersion(PalletConstant constant, string variableName, List<StatementSyntax> statements, ModuleVersion version, bool checkVersion = true)
      {
         PalletConstant childConstant = version.Module.Constants?.SingleOrDefault(x => x.Name == constant.Name);
         if (childConstant is null)
         {
            return;
         }

         string call = $"new {VersionnedModuleConstanteName(version.Version)}().{constant.Name}()";

         ExpressionStatementSyntax paramAffectation = SyntaxFactory.ExpressionStatement(
                             SyntaxFactory.AssignmentExpression(
                                 SyntaxKind.SimpleAssignmentExpression,
                                 SyntaxFactory.IdentifierName(variableName),
                                 SyntaxFactory.IdentifierName(call)
                             )
                         );

         if (checkVersion)
         {
            statements.Add(SyntaxFactory.IfStatement(
             SyntaxFactory.BinaryExpression(
                 SyntaxKind.EqualsExpression,
                 SyntaxFactory.IdentifierName("version"),
                 SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(version.Version))
              ),
             paramAffectation
            ));
         }
         else
         {
            AddLastVersionComment(statements);
            statements.Add(paramAffectation);
         }

      }

      private NamespaceDeclarationSyntax CreateErrors(NamespaceDeclarationSyntax namespaceDeclaration)
      {
         ClassName = Module.Name + "Errors";

         PalletErrors errors = Module.Errors;

         if (errors != null)
         {
            if (NodeTypes.TryGetValue(errors.TypeId, out NodeType nodeType))
            {
               var typeDef = nodeType as NodeTypeVariant;

               EnumDeclarationSyntax targetClass = SyntaxFactory.EnumDeclaration(ClassName)
                   .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)));

               if (typeDef.Variants != null)
               {
                  foreach (TypeVariant variant in typeDef.Variants)
                  {
                     EnumMemberDeclarationSyntax enumField = SyntaxFactory.EnumMemberDeclaration(variant.Name);

                     // add comment to field if exists
                     enumField = enumField.WithLeadingTrivia(GetCommentsRoslyn(variant.Docs, null, variant.Name));

                     targetClass = targetClass.AddMembers(enumField);
                  }
               }

               namespaceDeclaration = namespaceDeclaration.AddMembers(targetClass);
            }
         }

         return namespaceDeclaration;
      }

      private static string GetInvoceString(string returnType)
      {
         return "await _client.GetStorageAsync<" + returnType + ">(parameters, blockHash, token)";
      }

      private static InvocationExpressionSyntax GetStorageStringRoslyn(string module, string item, Storage.Type type, Storage.Hasher[] hashers = null)
      {
         var codeExpressions = new List<ArgumentSyntax>
          {
              SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(module))),
              SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(item))),
              SyntaxFactory.Argument(
                  SyntaxFactory.MemberAccessExpression(
                      SyntaxKind.SimpleMemberAccessExpression,
                      SyntaxFactory.IdentifierName("Substrate.NetApi.Model.Meta.Storage.Type"),
                      SyntaxFactory.IdentifierName(type.ToString())))
          };

         // if it is a map fill hashers and key
         if (hashers != null && hashers.Length > 0)
         {
            ExpressionSyntax keyReference = SyntaxFactory.ArrayCreationExpression(
                SyntaxFactory.ArrayType(
                    SyntaxFactory.IdentifierName("Substrate.NetApi.Model.Types.IType"),
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.ArrayRankSpecifier(
                            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                SyntaxFactory.OmittedArraySizeExpression())))),
                SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                        SyntaxFactory.IdentifierName("key"))));

            if (hashers.Length > 1)
            {
               keyReference = SyntaxFactory.MemberAccessExpression(
                   SyntaxKind.SimpleMemberAccessExpression,
                   SyntaxFactory.IdentifierName("key"),
                   SyntaxFactory.IdentifierName("Value")
               );
            }

            codeExpressions = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(module))),
                SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(item))),
                SyntaxFactory.Argument(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Substrate.NetApi.Model.Meta.Storage.Type"),
                        SyntaxFactory.IdentifierName(type.ToString()))),
                SyntaxFactory.Argument(
                    SyntaxFactory.ArrayCreationExpression(
                        SyntaxFactory.ArrayType(
                            SyntaxFactory.IdentifierName("Substrate.NetApi.Model.Meta.Storage.Hasher"),
                            SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier())),
                        SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                            SyntaxFactory.SeparatedList(
                                hashers.Select(p => SyntaxFactory.ParseExpression($"Substrate.NetApi.Model.Meta.Storage.Hasher.{p}")))))),
                SyntaxFactory.Argument(keyReference)
            };
         }

         return SyntaxFactory.InvocationExpression(
             SyntaxFactory.MemberAccessExpression(
                 SyntaxKind.SimpleMemberAccessExpression,
                 SyntaxFactory.IdentifierName("RequestGenerator"),
                 SyntaxFactory.IdentifierName("GetStorage")))
             .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(codeExpressions)));
      }

      private static ExpressionSyntax[] GetStorageMapStringRoslyn(string keyType, string returnType, string module, string item, Storage.Hasher[] hashers = null)
      {
         TypeOfExpressionSyntax typeofReturn = SyntaxFactory.TypeOfExpression(SyntaxFactory.IdentifierName(returnType));

         var result = new ExpressionSyntax[] {
              SyntaxFactory.ObjectCreationExpression(
                  SyntaxFactory.IdentifierName("System.Tuple<string,string>"),
                  SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                      new ArgumentSyntax[]
                      {
                          SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(module))),
                          SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(item)))
                      })),
                  null),
              SyntaxFactory.ObjectCreationExpression(
                  SyntaxFactory.IdentifierName("System.Tuple<Substrate.NetApi.Model.Meta.Storage.Hasher[], System.Type, System.Type>"),
                  SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                      new ArgumentSyntax[]
                      {
                          SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                          SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                          SyntaxFactory.Argument(typeofReturn)
                      })),
                  null)
          };

         // if it is a map fill hashers and key
         if (hashers != null && hashers.Length > 0)
         {
            ArrayCreationExpressionSyntax arrayExpression = SyntaxFactory.ArrayCreationExpression(
                SyntaxFactory.ArrayType(SyntaxFactory.IdentifierName("Substrate.NetApi.Model.Meta.Storage.Hasher[]")),
                SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                    SyntaxFactory.SeparatedList(hashers.Select(p =>
                        SyntaxFactory.ParseExpression($"Substrate.NetApi.Model.Meta.Storage.Hasher.{p}")))));

            TypeOfExpressionSyntax typeofType = SyntaxFactory.TypeOfExpression(SyntaxFactory.IdentifierName(keyType));

            result =
               new ExpressionSyntax[] {
                  SyntaxFactory.ObjectCreationExpression(
                      SyntaxFactory.IdentifierName("System.Tuple<string,string>"),
                      SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                          new ArgumentSyntax[]
                          {
                              SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(module))),
                              SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(item)))
                          })),
                      null),
                  SyntaxFactory.ObjectCreationExpression(
                      SyntaxFactory.IdentifierName("System.Tuple<Substrate.NetApi.Model.Meta.Storage.Hasher[], System.Type, System.Type>"),
                      SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
                          new ArgumentSyntax[]
                          {
                              SyntaxFactory.Argument(arrayExpression),
                              SyntaxFactory.Argument(typeofType),
                              SyntaxFactory.Argument(typeofReturn)
                          })),
                   null)
            };
         }

         return result;
      }

      private static ExpressionStatementSyntax AddPropertyValuesRoslyn(ExpressionSyntax[] exprs, string variableReference)
      {
         return SyntaxFactory.ExpressionStatement(
             SyntaxFactory.InvocationExpression(
                 SyntaxFactory.MemberAccessExpression(
                     SyntaxKind.SimpleMemberAccessExpression,
                     SyntaxFactory.IdentifierName(variableReference),
                     SyntaxFactory.IdentifierName("Add")),
                 SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(exprs.Select(SyntaxFactory.Argument)))));
      }
   }
}