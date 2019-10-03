﻿using System;

namespace Postulate.Integration.SqlServer
{
    public class DbObject
    {
        public DbObject()
        {
        }

        public DbObject(string schema, string name)
        {
            Schema = schema;
            Name = name;
        }

        public DbObject(string name)
        {
            var obj = Parse(name);
            Schema = obj.Schema;
            Name = obj.Name;
        }

        public static DbObject Parse(string name, string defaultSchema = "dbo")
        {
            var parts = name.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return new DbObject(parts[0], parts[1]);
            }
            else if (parts.Length == 1)
            {
                return new DbObject(defaultSchema, name);
            }

            throw new ArgumentException($"Couldn't parse name {name}");
        }

        public string Schema { get; set; }
        public string Name { get; set; }

        public override bool Equals(object obj)
        {
            var test = obj as DbObject;
            return (obj != null) ? test.Schema.ToLower().Equals(Schema.ToLower()) && test.Name.ToLower().Equals(Name.ToLower()) : false;
        }

        public override int GetHashCode()
        {
            return (Schema.ToLower() + "." + Name.ToLower()).GetHashCode();
        }
    }
}