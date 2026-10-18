#!/usr/bin/env node

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { randomBytes } from "node:crypto";
import { dirname, resolve } from "node:path";

const VALID_PRIORITIES = new Set(["p1", "p2", "p3", "p4", "p5", "p6", "p7"]);

function usage() {
  return [
    "Usage:",
    "  node .opencode/skills/create-kanban/scripts/add-cards.mjs --input <cards.json> [--board .kanban/board.json] [--settings .kanban/settings.json]",
    "",
    "cards.json must be an array of objects with title, summary, and optional priority."
  ].join("\n");
}

function parseArgs(argv) {
  const args = { board: ".kanban/board.json", input: undefined, settings: undefined };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--input") {
      args.input = argv[index + 1];
      index += 1;
      continue;
    }
    if (arg === "--board") {
      args.board = argv[index + 1];
      index += 1;
      continue;
    }
    if (arg === "--settings") {
      args.settings = argv[index + 1];
      index += 1;
      continue;
    }
    throw new Error(`Unknown argument: ${arg}`);
  }
  if (!args.input) {
    throw new Error("Missing --input <cards.json>.");
  }
  return args;
}

function readJson(filePath) {
  return JSON.parse(readFileSync(filePath, "utf8"));
}

function sanitizePriority(value) {
  const priority = String(value ?? "").toLowerCase();
  return VALID_PRIORITIES.has(priority) ? priority : "p4";
}

function sanitizeModelRef(value) {
  if (!value || typeof value !== "object") {
    return undefined;
  }
  const providerID = String(value.providerID ?? "").trim();
  const modelID = String(value.modelID ?? "").trim();
  if (!providerID || !modelID) {
    return undefined;
  }
  const reasoningDepth = String(value.reasoningDepth ?? "").trim();
  const reasoningDepthMode = String(value.reasoningDepthMode ?? "").trim();
  return {
    providerID,
    modelID,
    ...(reasoningDepth ? { reasoningDepth } : {}),
    ...(reasoningDepthMode ? { reasoningDepthMode } : {})
  };
}

function resolveBuildProfileModel(settings) {
  const workflowModel = settings?.settings?.workflow?.runnerProfiles?.build?.model;
  return sanitizeModelRef(workflowModel) ?? sanitizeModelRef(settings?.runnerProfiles?.build?.model);
}

function readBuildProfileModel(settingsPath) {
  if (!existsSync(settingsPath)) {
    return undefined;
  }
  return resolveBuildProfileModel(readJson(settingsPath));
}

function newId(now) {
  return `${now.toString(36)}-${randomBytes(5).toString("base64url").slice(0, 7).toLowerCase()}`;
}

function createCard(rawCard, now, defaultExecuteModel) {
  const title = String(rawCard?.title ?? "").trim();
  const summary = String(rawCard?.summary ?? "").trim();
  if (!title || !summary) {
    throw new Error("Each card requires a non-empty title and summary.");
  }
  return {
    id: newId(now),
    title,
    priority: sanitizePriority(rawCard.priority),
    summary,
    status: "todo",
    workflowMode: "default",
    createdAt: now,
    updatedAt: now,
    running: false,
    compacting: false,
    lastRunDurationMs: 0,
    ...(defaultExecuteModel ? { executeModel: { ...defaultExecuteModel } } : {})
  };
}

function validateBoard(board) {
  if (!board || typeof board !== "object") {
    throw new Error("Board file must contain a JSON object.");
  }
  if (board.schemaVersion !== 1 || board.fileType !== "kanban.board") {
    throw new Error("Board file must be schemaVersion 1 with fileType kanban.board.");
  }
  if (!board.boardState || !Array.isArray(board.boardState.cards) || !Array.isArray(board.boardState.sessions)) {
    throw new Error("Board file must contain boardState.cards and boardState.sessions arrays.");
  }
}

function createEmptyBoard() {
  return {
    schemaVersion: 1,
    fileType: "kanban.board",
    meta: {
      updatedAt: 0,
      revision: 0
    },
    boardState: {
      cards: [],
      sessions: []
    }
  };
}

function readBoard(boardPath) {
  if (!existsSync(boardPath)) {
    return createEmptyBoard();
  }
  const board = readJson(boardPath);
  validateBoard(board);
  return board;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const inputPath = resolve(args.input);
  const boardPath = resolve(args.board);
  const settingsPath = resolve(args.settings ?? `${dirname(boardPath)}/settings.json`);
  const rawCards = readJson(inputPath);
  if (!Array.isArray(rawCards) || rawCards.length === 0) {
    throw new Error("Input must be a non-empty array of cards.");
  }

  const board = readBoard(boardPath);

  const now = Date.now();
  const defaultExecuteModel = readBuildProfileModel(settingsPath);
  const cards = rawCards.map((card) => createCard(card, now, defaultExecuteModel));
  board.boardState.cards = [...cards, ...board.boardState.cards];
  board.meta = {
    ...(board.meta && typeof board.meta === "object" ? board.meta : {}),
    updatedAt: now,
    revision: Math.max(0, Number(board.meta?.revision) || 0) + 1
  };

  mkdirSync(dirname(boardPath), { recursive: true });
  writeFileSync(boardPath, `${JSON.stringify(board, null, 2)}\n`, "utf8");
  console.log(`Added ${cards.length} todo card(s) to ${boardPath}.`);
  for (const card of cards) {
    console.log(`- ${card.title} (${card.priority})`);
  }
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  console.error(usage());
  process.exitCode = 1;
}
