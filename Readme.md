# Expressionify

[![Nuget](https://img.shields.io/nuget/v/Clave.Expressionify)][1] [![Nuget](https://img.shields.io/nuget/dt/Clave.Expressionify)][1] [![Build Status](https://claveconsulting.visualstudio.com/Nugets/_apis/build/status/ClaveConsulting.Expressionify?branchName=master)][2] [![Azure DevOps tests](https://img.shields.io/azure-devops/tests/ClaveConsulting/Nugets/14)][2]

> Use extension methods in Entity Framework Core queries

## Installing

Install these two nuget packages:

- `Clave.Expressionify`
- `Clave.Expressionify.Generator`

Make sure to install the second one properly:

```xml
  <ItemGroup>
    <PackageReference Include="Clave.Expressionify" Version="6.6.0" />
    <PackageReference Include="Clave.Expressionify.Generator" Version="6.6.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
```

## How to use

0. Setup your database context with `.UseExpressionify()`
1. Mark the `public static` expression method with the `[Expressionify]` attribute.
2. Mark the class with the method as `partial`.
3. Use the extension method in the query

## Example

Lets say you have this code:

```csharp
var users = await db.Users
    .Where(user => user.DateOfBirth < DateTime.Now.AddYears(-18))
    .ToListAsync();
```

That second line is a bit long, so it would be nice to pull it out as a reusable extension method:

```csharp
public static class Extensions
{
    public static bool IsOver18(this User user)
        => user.DateOfBirth < DateTime.Now.AddYears(-18);
}

// ...

var users = await db.Users
    .Where(user => user.IsOver18())
    .ToListAsync();

```

Unfortunately this forces Entity Framework to run the query in memory, rather than in the database. That's not very efficient...

But, with just one additional line of code we can get Entity Framework to understand how translate our extension method to SQL

```diff
- public static class Extensions
+ public static partial class Extensions
  {
+     [Expressionify]
      public static bool IsOver18(this User user)
          => user.DateOfBirth < DateTime.Now.AddYears(-18);
  }

```

## Setup

The simplest way to add expressionify support is to configure the database context:

```csharp
services
    .AddDbContext<MyDbContext>(o => o
        .UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
        .UseExpressionify());
```

Make sure to call `.UseExpressionify()` after `.UseSqlServer()` (or whatever other sql provider you want to use).

The alternative is to only call `.Expressionify()` in the queries where you want it:

```csharp
var users = await db.Users
    .Expressionify()
    .Where(user => user.DateOfBirth < DateTime.Now.AddYears(-18))
    .ToListAsync();
```

### Query caching

When configuring the DbContext with `.UseExpressionify()`, Expressionify tries to use the EntityFramework query cache by default. This way the expression tree is only processed once and then cached.
However, this comes with [some limitations](#query-caching-limitations). Expressionify throws an exception if your query cannot be cached.
To fix this, you either have to call `.Expressionify()` explicitly on the query or disable query caching:

```csharp
.UseExpressionify(o => o.WithEvaluationMode(ExpressionEvaluationMode.FullCompatibilityButSlow));
```

## Upgrading from 3.1 to 5.0

Version 5 works with net 5.0, and has a few other changes. It relies on [Source generators](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) and Roslyn Analyzers for generating the code, instead of some very clumpsy msbuild code. This means that you will get help if you forget to mark the methods correctly.

These are the breaking changes:

- The class containing the method no longer needs to be `static`.
- The class containing the method now has to be marked as `partial`.
- The method no longer needs to be `public`, it can be private or internal.

## Limitations

Expressionify uses the Roslyn code analyzer and generator to look for `static` methods with expression bodies tagged with the `[Expressionify]` attribute in `partial` classes.

```csharp
public static partial class Extensions {
    // ✔ OK
    [Expressionify]
    public static int ToInt(this string value) => Convert.ToInt32(value);

    // ✔ OK (it can be private)
    [Expressionify]
    private static int ToInt(this string value) => Convert.ToInt32(value);

    // ❌ Not ok (it's not static)
    [Expressionify]
    public int ToInt(this string value) => Convert.ToInt32(value);

    // ❌ Not ok (it's missing the attribute)
    public static int ToInt(this string value) => Convert.ToInt32(value);

    // ❌ Not ok (it doesn't have an expression body)
    [Expressionify]
    public static int ToInt(this string value)
    {
        return Convert.ToInt32(value);
    }
}

// ❌ Not ok (it's not a partial class)
public static class Extensions {
    [Expressionify]
    public static int ToInt(this string value) => Convert.ToInt32(value);
}

```

### Query caching limitations

Using [query caching](#query-caching) works fine unless you introduce new query parameters in your `[Expressionify]` method. In that case you'll get an `InvalidOperationException` telling you to explicitly call `.Expressionify()` on the query, as the query cannot be translated.

Examples:

```csharp
public static partial class Extensions {
   // Example: users.Where(u => u.IsOver18())
   // ✔ OK for ExpressionEvaluationMode.FullCompatibilityButSlow
   // ✔ OK for ExpressionEvaluationMode.LimitedCompatibilityButCached
   // The expression can be translated to SQL without introducing new parameters
   [Expressionify]
   public static bool IsOver18(this User user)
       => user.DateOfBirth < DateTime.Now.AddYears(-18);

   // Example: users.Where(u => u.IsOlderThan(18))
   // ✔ OK for ExpressionEvaluationMode.FullCompatibilityButSlow
   // ✔ OK for ExpressionEvaluationMode.LimitedCompatibilityButCached
   // The parameter 'years' is already present in the query itself. No new parameters are introduced when expanding the query.
   [Expressionify]
   public static bool IsOlderThan(this User user, int years)
       => user.DateOfBirth < DateTime.Now.AddYears(-years);

   // Example: users.Where(u => u.WasAddedRecently())
   // ✔ OK for ExpressionEvaluationMode.FullCompatibilityButSlow
   // ❌ Not ok for ExpressionEvaluationMode.LimitedCompatibilityButCached
   // ✔ OK for ExpressionEvaluationMode.LimitedCompatibilityButCached when explicitly expanding the query with 'query.Expressionify()'
   // 'TimeProvider.UtcNow' is a new parameter that is not known in the query before calling '.Expressionify()'.
   [Expressionify]
   public static bool WasAddedRecently(this User user)
       => user.Created >= TimeProvider.UtcNow.AddDays(-1);

   // Example: users.Select(u => u.ToTestView(null))
   // ✔ OK for ExpressionEvaluationMode.FullCompatibilityButSlow
   // ❌ Not ok for ExpressionEvaluationMode.LimitedCompatibilityButCached
   // ✔ OK for ExpressionEvaluationMode.LimitedCompatibilityButCached when explicitly expanding the query with 'query.Expressionify()'
   // With the input 'null' on the address, the expression 'address == null ? null : address.Street' gets replaced with a
   // new parameter for the value 'null'.
   [Expressionify]
   public static TestView ToTestView(this TestEntity testEntity, TestAddress? address)
       => new() { Name = testEntity.Name, Street = address == null ? null : address.Street };
}
```

## Inspiration and help

The first part of this project relies heavily on the work done by [Luke McGregor](https://twitter.com/staticv0id) in his [LinqExpander](https://github.com/lukemcgregor/LinqExpander) project, as described in his article on [composable repositories - nesting expressions](https://blog.staticvoid.co.nz/2016/composable_repositories_-_nesting_extensions/), and on the updated code by [Ben Cull](https://twitter.com/BenWhoLikesBeer) in his article [Expression and Projection Magic for Entity Framework Core](https://benjii.me/2018/01/expression-projection-magic-entity-framework-core/).

The second part of this project uses Roslyn to analyze and generate code, and part of it is built directly on code by [Carlos Mendible](https://twitter.com/cmendibl3) from his article [Create a class with .NET Core and Roslyn](https://carlos.mendible.com/2017/03/02/create-a-class-with-net-core-and-roslyn/).

The rest is stitched together from various Stack Overflow answers and code snippets found on GitHub.

[1]: https://www.nuget.org/packages/Clave.Expressionify/
[2]: https://claveconsulting.visualstudio.com/Nugets/_build/latest?definitionId=14
