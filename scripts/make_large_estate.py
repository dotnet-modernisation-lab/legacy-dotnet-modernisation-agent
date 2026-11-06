"""Generates a large synthetic .NET Framework estate for scale-testing the assessment tool.

Usage: python scripts/make_large_estate.py <outputDirectory> [seed]

The estate is invented (nothing here is from a real application). It has several websites with many pages, a shared logic library
with folders that depend on each other (including a loop), a data library whose classes run stored procedures and inline SQL, SQL files
defining the procedures over a shared set of tables, and a page that reaches the data layer directly. The same seed gives the same estate.
"""
import os
import random
import sys

out = sys.argv[1]
seed = int(sys.argv[2]) if len(sys.argv) > 2 else 7
rng = random.Random(seed)

SITES = ["Storefront", "BackOffice", "Partner"]
PAGES_PER_SITE = 40
FOLDERS = ["Orders", "Billing", "Customers", "Inventory", "Reporting", "Common"]
CLASSES_PER_FOLDER = 12
TABLES = [f"T{n:02d}" for n in range(1, 41)]
PROCS = [f"usp_Proc{n:02d}" for n in range(1, 31)]


def write(path, text):
    full = os.path.join(out, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def project(name, items, references=(), web=False, system=()):
    refs = "".join(f'<Reference Include="{r}" />' for r in system)
    projects = "".join(f'<ProjectReference Include="..\\{r}\\{r}.csproj" />' for r in references)
    body = "".join(items)
    guids = "<ProjectTypeGuids>{349c5851-65df-11da-9384-00065b846f21};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>" if web else ""
    return (
        '<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">\n'
        f"  <PropertyGroup>{guids}<OutputType>Library</OutputType><AssemblyName>{name}</AssemblyName><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup>\n"
        f"  <ItemGroup>{refs}</ItemGroup>\n  <ItemGroup>{projects}{body}</ItemGroup>\n</Project>\n"
    )


# ---- Data library: repositories that run procedures and inline SQL -----------------------------------
repos = []
for n in range(1, 21):
    proc = rng.choice(PROCS)
    table = rng.choice(TABLES)
    write(f"src/Estate.Data/Repositories/Repo{n:02d}Dal.cs", f"""using System.Data.SqlClient;
namespace Estate.Data
{{
    public class Repo{n:02d}Dal
    {{
        public void Load(int id)
        {{
            var command = new SqlCommand("EXEC dbo.{proc}");
            command.ExecuteNonQuery();
        }}

        public void Save(int id)
        {{
            var command = new SqlCommand("INSERT INTO dbo.{table} (Id) VALUES (@id)");
            command.ExecuteNonQuery();
        }}
    }}
}}
""")
    repos.append(f"Repo{n:02d}Dal")
write("src/Estate.Data/Estate.Data.csproj", project("Estate.Data", [f'<Compile Include="Repositories\\{r}.cs" />' for r in repos], system=["System.Data"]))

# ---- stored procedure definitions over shared tables ----------------------------------------------------
for proc in PROCS:
    reads = rng.sample(TABLES, 2)
    write_to = rng.sample(TABLES, rng.choice([0, 1, 1, 2]))
    body = "".join(f"    SELECT * FROM dbo.{t};\n" for t in reads) + "".join(f"    UPDATE dbo.{t} SET Touched = 1;\n" for t in write_to)
    write(f"db/{proc}.sql", f"CREATE PROCEDURE dbo.{proc}\nAS\nBEGIN\n{body}END\nGO\n")
for table in TABLES:
    write(f"db/tables/{table}.sql", f"CREATE TABLE dbo.{table} (Id INT NOT NULL, Touched BIT NULL)\nGO\n")

# ---- Logic library: folders that depend on each other, with a loop Orders <-> Billing ---------------------
all_classes = {f: [f"{f}Service{n:02d}" for n in range(1, CLASSES_PER_FOLDER + 1)] for f in FOLDERS}
loop = {"Orders": "Billing", "Billing": "Orders"}
items = []
for folder, names in all_classes.items():
    for index, name in enumerate(names):
        callee_folder = loop.get(folder) if index == 0 and folder in loop else rng.choice(FOLDERS)
        callee = rng.choice(all_classes[callee_folder])
        repo = rng.choice(repos)
        write(f"src/Estate.Core/{folder}/{name}.cs", f"""using Estate.Data;
namespace Estate.Core
{{
    public class {name}
    {{
        private readonly {callee} next = new {callee}();
        private readonly {repo} data = new {repo}();

        public void Run(int id)
        {{
            data.Save(id);
            if (id > 0) {{ next.Run(id - 1); }}
        }}
    }}
}}
""")
        items.append(f'<Compile Include="{folder}\\{name}.cs" />')
write("src/Estate.Core/Estate.Core.csproj", project("Estate.Core", items, references=["Estate.Data"]))

# ---- websites ---------------------------------------------------------------------------------------------
for site in SITES:
    compiles, contents = [], []
    for n in range(1, PAGES_PER_SITE + 1):
        folder = rng.choice(FOLDERS)
        service = rng.choice(all_classes[folder])
        page = f"Page{n:02d}"
        direct = site == "Partner" and n == 1  # one page reaches the data layer directly
        field = rng.choice(repos) if direct else service
        write(f"src/Estate.{site}/Pages/{page}.aspx", f'<%@ Page Language="C#" CodeBehind="{page}.aspx.cs" Inherits="Estate.{site}.{page}Screen" %>\n<html><body><form runat="server"><asp:Button ID="Go" runat="server" OnClick="Go{n:02d}_Click" /></form></body></html>\n')
        write(f"src/Estate.{site}/Pages/{page}.aspx.cs", f"""using System;
using System.Web.UI;
using Estate.{'Data' if direct else 'Core'};
namespace Estate.{site}
{{
    public class {page}Screen : Page
    {{
        private readonly {field} worker = new {field}();

        protected void Go{n:02d}_Click(object sender, EventArgs e)
        {{
            Session["Last{n:02d}"] = Request.Form["q{n:02d}"];
            worker.{'Save' if direct else 'Run'}({n});
        }}
    }}
}}
""")
        contents.append(f'<Content Include="Pages\\{page}.aspx" />')
        compiles.append(f'<Compile Include="Pages\\{page}.aspx.cs" />')
    refs = ["Estate.Core", "Estate.Data"] if site == "Partner" else ["Estate.Core"]
    write(f"src/Estate.{site}/Estate.{site}.csproj", project(f"Estate.{site}", contents + compiles, references=refs, web=True, system=["System.Web"]))

print(f"generated seed {seed} in {out}")
