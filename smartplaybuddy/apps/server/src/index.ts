import { createServer } from "./server.js";

const PORT = Number(process.env.PORT ?? 2508);

createServer(PORT);
