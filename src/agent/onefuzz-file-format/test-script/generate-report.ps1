cargo run --example cobertura > coverage.xml

# To install:
#
#     dotnet tool install --global dotnet-reportgenerator-globaltool --version 4.6.1
#
reportgenerator.exe -sourcedirs:test-data -reports:coverage.xml  -targetdir:reports -reporttypes:HtmlInline_AzurePipelines
