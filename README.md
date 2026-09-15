![Futaba](https://img.shields.io/badge/Futaba-778868)
[![NuGet version (Spannerisms.Futaba)](https://img.shields.io/nuget/v/Spannerisms.Futaba.svg)](https://www.nuget.org/packages/Spannerisms.Futaba/)

# Futaba

Futaba is an open-source, low-level assembler specifically targetting the Super Famicom and Super Nintendo Entertainment System.

## Download

Download the `.zip` file that matches your architecture from [the releases page](https://github.com/spannerisms/Futaba/releases/latest).

If you want to use the core library in your own project, it is avaible as [Spannerisms.Futaba](https://www.nuget.org/packages/Spannerisms.Futaba) on nuget.org.

## Basic usage

For extensive documentation of syntax or the command line interface, please see [this page](https://spannerisms.github.io/futaba).

### For developers

Generally, you should make one `Assembler` object per assembly target. The assembler types are optimized for repeated builds of the same file; instantiating a new assembler puts items on the large object heap, which can effect significant performance issues if this induces a collection. Note that the `Assembler` type is an `IDisposable`, as it tracks resources in unmanaged memory.



```csharp
using Assembler assembler = new LoromAssembler("main.asm");

byte[] assembledRom = assembler.Assembler();

if (assembler.HasErrors) {
    // abort
}

```

----

*This project is not affiliated with nor endorsed by Nintendo.*
