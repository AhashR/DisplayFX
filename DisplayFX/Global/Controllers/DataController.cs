using System;
using System.IO;
using System.Reflection;
using FluentResults;
using Newtonsoft.Json;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Global.Controllers;

public class DataController
{
    private static readonly string _directory = AppContext.BaseDirectory;

    public string DataPath => Path.Combine(_directory, @"Data\Data.json");

    public void Write(Computer data)
    {
        var serializeObject = JsonConvert.SerializeObject(data, new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects
        });

        File.WriteAllText(DataPath, serializeObject);
    }

    public Result<Computer> Load()
    {
        try
        {
            if (!File.Exists(DataPath))
                return Result.Fail(new Error("Data file not found."));

            using StreamReader reader = new(DataPath);
            var json = reader.ReadToEnd();
            var computer = JsonConvert.DeserializeObject<Computer>(json);

            return computer is null ? Result.Fail(new Error("Failed to deserialize computer.")) : Result.Ok(computer);
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error(ex.Message));
        }
    }
}