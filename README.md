# Substrate .NET Toolchain
[![GitHub issues](https://img.shields.io/github/issues/SubstrateGaming/Substrate.NET.Toolchain.svg)](https://github.com/SubstrateGaming/Substrate.NET.Toolchain/issues) 
[![license](https://img.shields.io/github/license/SubstrateGaming/Substrate.NET.Toolchain)](./LICENSE)
[![contributors](https://img.shields.io/github/contributors/SubstrateGaming/Substrate.NET.Toolchain)](https://github.com/SubstrateGaming/Substrate.NET.Toolchain/graphs/contributors)  
  
**Substrate .NET Toolchain model-driven SDK generator for substrate-based nodes** 
![darkfriend77_substrate_gaming](https://user-images.githubusercontent.com/17710198/227789154-e8ecaaf9-ce68-4f2a-ad2e-5711e3c9fca0.png)

# What is the Substrate .NET Toolchain ?

Substrate .NET Toolchain is a .NET toolchain featuring .NET framework extensions and code generation utilities to build substrate storage services and clients quickly. This toolchain ideally extends [Substrate.NET.API](https://github.com/SubstrateGaming/Substrate.NetApi) library, which provides raw access to substrate nodes.

![image](https://user-images.githubusercontent.com/17710198/221981597-de89c308-8f33-4c08-a463-3270e68a5035.png)

## Important
This toolchain is under development, and things may change quickly.

## Projects
Below is a high-level technical overview of the libraries and tools available in Substrate .NET Toolchain.

| Project | Description                                                                                                                                                                                                                                                                               | NuGet 
|---|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---|
| Substrate.ServiceLayer | Implements the fundamental layer to access substrate node storage changes with a convenient API.                                                                                                                                                                                          | [![Nuget](https://img.shields.io/nuget/v/Substrate.ServiceLayer)](https://www.nuget.org/packages/Substrate.ServiceLayer/) |
| Substrate.ServiceLayer.Model | Implements standard classes to easily share types between services and clients.                                                                                                                                                                                                           | [![Nuget](https://img.shields.io/nuget/v/Substrate.ServiceLayer.Model)](https://www.nuget.org/packages/Substrate.ServiceLayer.Model/) |
| Substrate.AspNetCore | Implements extensions to the service layer that allow for quickly building a RESTful service to access your substrate node storage.                                                                                                                                                       | [![Nuget](https://img.shields.io/nuget/v/Substrate.AspNetCore)](https://www.nuget.org/packages/Substrate.AspNetCore/) |
| Substrate.DotNet, Substrate.DotNet.Template | .NET developer toolchain to scaffold actual projects such as a RESTful service including all the storage classes, types, and consumer clients. The projects generated with the generator toolchain are intended to be used for scaffolding and starting a substrate node service quickly. | [![Nuget](https://img.shields.io/nuget/v/Substrate.DotNet)](https://www.nuget.org/packages/Substrate.DotNet/) [![Nuget](https://img.shields.io/nuget/v/Substrate.DotNet.Template)](https://www.nuget.org/packages/Substrate.DotNet.Template/)|

## Architecture

![image](https://user-images.githubusercontent.com/17710198/229302672-da3d4ddf-b2fa-4f65-bdb2-2210d8daf1e9.png)

## Getting Started

Assuming your [substrate node is running locally](https://github.com/paritytech/substrate/), you're ready to build your services and clients using the Substrate .NET Toolchain.

---

### Installing the template

---

Install our .NET template with the following command:

```sh
dotnet new install Substrate.DotNet.Template
```

which makes `dotnet new substrate` available.

### Scaffolding a project

---

Using a terminal of your choice, create a new directory for your project and execute the following command in that directory:

```sh
dotnet new sln
dotnet new substrate \
   --sdk_version 0.4.3 \
   --rest_service PROJECTNAME.RestService \
   --net_api PROJECTNAME.NetApiExt \
   --rest_client PROJECTNAME.RestClient \
   --metadata_websocket ws://127.0.0.1:9944 \
   --generate_openapi_documentation true \
   --force \
   --allow-scripts yes
```

which generates a new solution and a couple of .NET projects in your project directory. 
(A description for all command parameters can be found [here](Tools/Substrate.DotNet.Template/README.md))
    

```txt
.
├─── .substrate
├─── .config
├─── PROJECTNAME.NetApiExt
├─── PROJECTNAME.RestClient
├─── PROJECTNAME.RestClient.Mockup
├─── PROJECTNAME.RestClient.Test
├─── PROJECTNAME.RestService
```

### Role of the Generated Projects

Before elaborating on each of the generated projects, let’s first talk about [Substrate.NetApi](https://github.com/SubstrateGaming/Substrate.NET.API/tree/master/Substrate.NetApi) which is the basis that these projects are built upon.

#### Substrate .NET API

`Substrate.NetApi` is the basic framework for accessing and handling JSON-RPC connections and handling all standard RPC calls exposed by the `rpc.methods()` of every substrate node. It additionally implements Rust primitives and Generics as a C# representation like [U8](https://github.com/SubstrateGaming/Substrate.NET.API/blob/master/Substrate.NetApi/Model/Types/Primitive/U8.cs), [BaseVec](https://github.com/SubstrateGaming/Substrate.NET.API/blob/master/Substrate.NetApi/Model/Types/Base/BaseVec.cs) (Vec<>), or [EnumExt](https://github.com/SubstrateGaming/Substrate.NET.API/blob/master/Substrate.NetApi/Model/Types/Base/BaseEnumExt.cs) (Rust-specific Enums). 


#### Substrate .NET API Extension

Since `Substrate.NetApi` has no other types than the ones previously described, accessing a node’s storage or sending extrinsic would involve manually creating the necessary types. This is where the generated `Substrate.NetApiExt` comes into play since it extends `Substrate.NetApi` by exposing all the node-specific types, storage access, extrinsic calls and more. 


#### Substrate REST Service

This service:

 - Connects to a node and subscribes to the global storage changes, which are then maintained in memory.
 - Offers a REST service (poll) which exposes all the storage information as REST.
 - Offers a subscription service (pub/sub) providing changes over a WebSocket. 

The benefit of this approach is that this artifact is much more lightweight than the node itself and can therefore be scaled according to the needs of the consumers without putting any load on an RPC node except for one connection (per RestService instance) for the global storage subscription.


#### Substrate REST Client

This RestClient can be used in a C#, Unity, or any other application allowing it to access the information provided by the previously described RestService. Using the RestClient one could subscribe to the node storage changes using the WebSocket or access the storage directly through exposed REST service.

As you can see, we could in principle launch any service or create any application on top of Substrate without any further knowledge except from the library usage.

The generated projects contain everything you need in order to get started making excellent substrate services and clients in C# and the .NET framework.


### Video Tutorial

You can also watch our short step-by-step tutorial that guides you through the entire process.

[![IMAGE ALT TEXT HERE](https://img.youtube.com/vi/27k8vxCrXcY/0.jpg)](https://www.youtube.com/watch?v=27k8vxCrXcY)

### Examples
 - AstarNET
 ```sh
   dotnet new substrate \
      --sdk_version 0.4.3 \  
      --rest_service AstarNET.RestService \  
      --net_api AstarNET.NetApiExt \  
      --rest_client AstarNET.RestClient \  
      --metadata_websocket wss://rpc.astar.network \  
      --generate_openapi_documentation false \  
      --force \  
      --allow-scripts yes
 ```
![astarNET](https://user-images.githubusercontent.com/17710198/228181453-b0cb6e15-8681-4cbe-b330-b265ecf00847.gif)

## Documents

- [Contributing](./CONTRIBUTING.md)
- [Development](./DEVELOPMENT.md)
- [Examples](./EXAMPLES.md)
- `dotnet substrate` toolchain with [Substrate.DotNet](/Tools/Substrate.DotNet/README.md)
- `dotnet new substrate` template with [Substrate.DotNet.Template](/Tools/Substrate.DotNet.Template/README.md)
