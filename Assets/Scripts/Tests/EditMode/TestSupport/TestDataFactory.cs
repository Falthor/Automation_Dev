using Game.Data;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.TestSupport
{
    /// <summary>
    /// Builds ScriptableObject content assets with specific field values for tests, via
    /// SerializedObject (the private fields have no production setters - this is test-only
    /// scaffolding, not a production API). Editor-only, matching Game.Tests.EditMode's own scope.
    /// </summary>
    public static class TestDataFactory
    {
        public static ItemDefinition NewItem(string id, ItemType type = ItemType.Component)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            var so = new SerializedObject(item);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("type").enumValueIndex = (int)type;
            so.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        public static ItemDatabase NewItemDatabase(params ItemDefinition[] items)
        {
            var database = ScriptableObject.CreateInstance<ItemDatabase>();
            var so = new SerializedObject(database);
            SerializedProperty array = so.FindProperty("items");
            array.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return database;
        }

        public static RecipeDefinition NewRecipe(string id, float timeSeconds, float computeCost, int outputAmount, params (ItemDefinition item, int amount)[] ingredients)
        {
            return NewRecipe(id, timeSeconds, computeCost, outputAmount, null, ingredients);
        }

        public static RecipeDefinition NewRecipe(string id, float timeSeconds, float computeCost, int outputAmount, ResearchDefinition unlockResearch, params (ItemDefinition item, int amount)[] ingredients)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            var so = new SerializedObject(recipe);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("timeSeconds").floatValue = timeSeconds;
            so.FindProperty("computeCost").floatValue = computeCost;
            so.FindProperty("outputAmount").intValue = outputAmount;
            so.FindProperty("unlockResearch").objectReferenceValue = unlockResearch;

            SerializedProperty array = so.FindProperty("ingredients");
            array.arraySize = ingredients.Length;
            for (int i = 0; i < ingredients.Length; i++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("item").objectReferenceValue = ingredients[i].item;
                element.FindPropertyRelative("amount").intValue = ingredients[i].amount;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }

        public static ResearchDefinition NewResearch(string id, float cost)
        {
            var research = ScriptableObject.CreateInstance<ResearchDefinition>();
            var so = new SerializedObject(research);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("cost").floatValue = cost;
            so.ApplyModifiedPropertiesWithoutUndo();
            return research;
        }

        static void SetStringArray(SerializedObject so, string propertyName, string[] values)
        {
            SerializedProperty array = so.FindProperty(propertyName);
            array.arraySize = values?.Length ?? 0;
            for (int i = 0; i < array.arraySize; i++)
            {
                array.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }

        public static RecipeDatabase NewRecipeDatabase(params RecipeDefinition[] recipes)
        {
            var database = ScriptableObject.CreateInstance<RecipeDatabase>();
            var so = new SerializedObject(database);
            SerializedProperty array = so.FindProperty("recipes");
            array.arraySize = recipes.Length;
            for (int i = 0; i < recipes.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = recipes[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return database;
        }

        public static FoundryDefinition NewFoundry(int maxStackPerItem, float powerDemandKw, float intakeIntervalSeconds, params string[] recipeIds)
        {
            var foundry = ScriptableObject.CreateInstance<FoundryDefinition>();
            var so = new SerializedObject(foundry);
            so.FindProperty("maxStackPerItem").intValue = maxStackPerItem;
            so.FindProperty("powerDemandKw").floatValue = powerDemandKw;
            so.FindProperty("intakeIntervalSeconds").floatValue = intakeIntervalSeconds;

            SerializedProperty array = so.FindProperty("recipeIds");
            array.arraySize = recipeIds.Length;
            for (int i = 0; i < recipeIds.Length; i++)
            {
                array.GetArrayElementAtIndex(i).stringValue = recipeIds[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return foundry;
        }

        public static FactoryDefinition NewFactory(int maxStackPerItem, float powerDemandKw, string[] recipeIds, string[] acceptedItemIds)
        {
            var factory = ScriptableObject.CreateInstance<FactoryDefinition>();
            var so = new SerializedObject(factory);
            so.FindProperty("maxStackPerItem").intValue = maxStackPerItem;
            so.FindProperty("powerDemandKw").floatValue = powerDemandKw;
            SetStringArray(so, "recipeIds", recipeIds);
            SetStringArray(so, "acceptedItemIds", acceptedItemIds);
            so.ApplyModifiedPropertiesWithoutUndo();
            return factory;
        }

        public static AssemblerDefinition NewAssembler(int maxStackPerItem, float powerDemandKw, string[] recipeIds, string[] acceptedItemIds, ResearchDefinition unlockResearch)
        {
            var assembler = ScriptableObject.CreateInstance<AssemblerDefinition>();
            var so = new SerializedObject(assembler);
            so.FindProperty("maxStackPerItem").intValue = maxStackPerItem;
            so.FindProperty("powerDemandKw").floatValue = powerDemandKw;
            so.FindProperty("unlockResearch").objectReferenceValue = unlockResearch;
            SetStringArray(so, "recipeIds", recipeIds);
            SetStringArray(so, "acceptedItemIds", acceptedItemIds);
            so.ApplyModifiedPropertiesWithoutUndo();
            return assembler;
        }

        public static AdvancedFoundryDefinition NewAdvancedFoundry(int maxStackPerItem, float powerDemandKw, string[] recipeIds, string[] acceptedItemIds)
        {
            var advancedFoundry = ScriptableObject.CreateInstance<AdvancedFoundryDefinition>();
            var so = new SerializedObject(advancedFoundry);
            so.FindProperty("maxStackPerItem").intValue = maxStackPerItem;
            so.FindProperty("powerDemandKw").floatValue = powerDemandKw;
            SetStringArray(so, "recipeIds", recipeIds);
            SetStringArray(so, "acceptedItemIds", acceptedItemIds);
            so.ApplyModifiedPropertiesWithoutUndo();
            return advancedFoundry;
        }

        public static PowerplantGazDefinition NewPowerplantGaz(ItemDefinition fuelItem, int maxFuelStack, float powerOutputKw, float selfPowerDemandKw, float cuDemand, float fuelCycleTimeSeconds)
        {
            var powerplant = ScriptableObject.CreateInstance<PowerplantGazDefinition>();
            var so = new SerializedObject(powerplant);
            so.FindProperty("fuelItem").objectReferenceValue = fuelItem;
            so.FindProperty("maxFuelStack").intValue = maxFuelStack;
            so.FindProperty("powerOutputKw").floatValue = powerOutputKw;
            so.FindProperty("selfPowerDemandKw").floatValue = selfPowerDemandKw;
            so.FindProperty("cuDemand").floatValue = cuDemand;
            so.FindProperty("fuelCycleTimeSeconds").floatValue = fuelCycleTimeSeconds;
            so.ApplyModifiedPropertiesWithoutUndo();
            return powerplant;
        }

        public static LaboratoryDefinition NewLaboratory(ItemDefinition cardItem, int maxCardStack, float powerDemandKw, float cuDemand, float cardConvertIntervalSeconds, float rpPerCard)
        {
            var laboratory = ScriptableObject.CreateInstance<LaboratoryDefinition>();
            var so = new SerializedObject(laboratory);
            so.FindProperty("cardItem").objectReferenceValue = cardItem;
            so.FindProperty("maxCardStack").intValue = maxCardStack;
            so.FindProperty("powerDemandKw").floatValue = powerDemandKw;
            so.FindProperty("cuDemand").floatValue = cuDemand;
            so.FindProperty("cardConvertIntervalSeconds").floatValue = cardConvertIntervalSeconds;
            so.FindProperty("rpPerCard").floatValue = rpPerCard;
            so.ApplyModifiedPropertiesWithoutUndo();
            return laboratory;
        }

        public static DataCenterDefinition NewDataCenter(int maxStackPerItem, string[] acceptedItemIds, ResearchDefinition unlockResearch)
        {
            var dataCenter = ScriptableObject.CreateInstance<DataCenterDefinition>();
            var so = new SerializedObject(dataCenter);
            so.FindProperty("maxStackPerItem").intValue = maxStackPerItem;
            so.FindProperty("unlockResearch").objectReferenceValue = unlockResearch;
            SetStringArray(so, "acceptedItemIds", acceptedItemIds);
            so.ApplyModifiedPropertiesWithoutUndo();
            return dataCenter;
        }
    }
}
