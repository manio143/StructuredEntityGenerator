using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Stride.Assets.Entities;
using Stride.Core.Diagnostics;
using Stride.Core.Yaml;
using Stride.Engine;

namespace StructuredEntityGenerator
{
    [Generator]
    public class StructuredEntityGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("CL_", "", "Starting generator", "General", DiagnosticSeverity.Info, true), null));

            var logger = new CompilerLogger(context);
            var entities = GetLoadOptions(context);
            foreach(var entity in entities)
            {
                logger.Info($"Compiling structured entity '{entity.File.Path}'");
                var classes = GenerateCode(entity, logger);
                foreach(var @class in classes)
                {
                    context.AddSource(@class.Name, SourceText.From(@class.Code));
                }
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        private static List<Class> GenerateCode(StructuredEntity structuredEntity, ILogger logger)
        {
            var prefab = YamlSerializer.Load<PrefabAsset>(structuredEntity.File.Path, logger);
            var classes = new List<Class>();

            foreach (var entity in prefab.Hierarchy.RootParts)
                GenerateCode(entity, null, structuredEntity.IncludeComponents, classes, logger);

            return classes;
        }

        private static void GenerateCode(Entity entity, string parentName, bool components, List<Class> classes, ILogger logger)
        {
            var @class = new Class() { Name = $"{parentName ?? string.Empty}_{entity.Name}" };

            foreach (var child in entity.GetChildren())
                GenerateCode(child, @class.Name, components, classes, logger);

            @class.Code = @$"using Stride.Engine;

namespace Prefab
{{
    public class {@class.Name}
    {{
        public Entity Entity {{ get; }}

        {entity.GetChildren().Select(child => $"public {$"{@class.Name}_{child.Name}"} {child.Name} {{ get; }}\n")}

        public {@class.Name}(Entity entity)
        {{
            this.Entity = entity;
            {entity.GetChildren().Select(child => $"this.{child.Name} = new {$"{@class.Name}_{child.Name}"}(entity.GetChildren().Single(child => child.Name == \"{child.Name}\"));\n")}
        }}
    }}
}}";

            classes.Add(@class);
        }

        private static IEnumerable<StructuredEntity> GetLoadOptions(GeneratorExecutionContext context)
        {
            foreach (AdditionalText file in context.AdditionalFiles)
            {
                if (Path.GetExtension(file.Path).Equals(".sdprefab", StringComparison.OrdinalIgnoreCase))
                {
                    // are there any options for it?
                    if (context.AnalyzerConfigOptions.GetOptions(file)
                        .TryGetValue("build_metadata.additionalfiles.AsStructuredEntity", out string asStructuredEntity) && bool.TryParse(asStructuredEntity, out bool compile) && compile)
                    {

                        context.AnalyzerConfigOptions.GetOptions(file)
                            .TryGetValue("build_metadata.additionalfiles.IncludeComponents", out string includeComponentsString);
                        bool.TryParse(includeComponentsString, out bool includeComponents);

                        yield return new StructuredEntity { File = file, IncludeComponents = includeComponents };
                    }
                }
            }
        }

        private class CompilerLogger : ILogger
        {
            private readonly GeneratorExecutionContext context;
            public CompilerLogger(GeneratorExecutionContext context) => this.context = context;
            public string Module => nameof(StructuredEntityGenerator);

            public void Log(ILogMessage logMessage)
            {
                context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("CL_", logMessage.Module, logMessage.Text, "General", ToSeverity(logMessage.Type), true), null));
            }

            private static DiagnosticSeverity ToSeverity(LogMessageType level)
                => level switch
                {
                    LogMessageType.Debug => DiagnosticSeverity.Hidden,
                    LogMessageType.Verbose => DiagnosticSeverity.Hidden,
                    LogMessageType.Info => DiagnosticSeverity.Info,
                    LogMessageType.Warning => DiagnosticSeverity.Warning,
                    LogMessageType.Error => DiagnosticSeverity.Error,
                    LogMessageType.Fatal => DiagnosticSeverity.Error,
                    _ => throw new NotSupportedException(),
                };
        }

        private struct StructuredEntity
        {
            public AdditionalText File { get; set; }
            public bool IncludeComponents { get; set; }
        }

        private struct Class
        {
            public string Name { get; set; }
            public string Code { get; set; }
        }
    }
}
