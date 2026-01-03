const { MongoClient } = require('mongodb');
const fs = require('fs');
const path = require('path');

const MONGO_URI = 'mongodb://localhost:27017';
const DB_NAME = 'TaskPaneAddin';
const COLLECTION_NAME = 'PromptPresetCollection';

async function importPrompts() {
  const client = new MongoClient(MONGO_URI);

  try {
    await client.connect();
    console.log('Connected to MongoDB');

    const db = client.db(DB_NAME);
    const collection = db.collection(COLLECTION_NAME);

    // Read the refactored prompts
    const filePath = path.join(__dirname, 'RefactoredPrompts.json');
    const data = fs.readFileSync(filePath, 'utf8');
    const prompts = JSON.parse(data);

    // Delete existing documents
    const deleteResult = await collection.deleteMany({});
    console.log(`Deleted ${deleteResult.deletedCount} existing prompts`);

    // Insert new prompts
    const insertResult = await collection.insertMany(prompts);
    console.log(`Inserted ${insertResult.insertedCount} new prompts`);

    // Verify
    const count = await collection.countDocuments({});
    console.log(`Total prompts in collection: ${count}`);

    // Show inserted IDs
    console.log('Inserted document IDs:', Object.keys(insertResult.insertedIds).map(i => insertResult.insertedIds[i]));

  } catch (error) {
    console.error('Error:', error.message);
    process.exit(1);
  } finally {
    await client.close();
    console.log('Connection closed');
  }
}

importPrompts();
