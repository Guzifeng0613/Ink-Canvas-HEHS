// @parity src/InkCanvas.Core/History/UndoStack.cs
export interface UndoAction {
  Name: string;
  Apply(): void;
  Revert(): void;
}

export class UndoStack {
  Capacity: number;
  private undoActions: UndoAction[] = [];
  private redoActions: UndoAction[] = [];

  constructor(capacity: number = 100) {
    this.Capacity = Math.max(1, capacity);
  }

  get CanUndo(): boolean {
    return this.undoActions.length > 0;
  }

  get CanRedo(): boolean {
    return this.redoActions.length > 0;
  }

  get UndoCount(): number {
    return this.undoActions.length;
  }

  get RedoCount(): number {
    return this.redoActions.length;
  }

  push(action: UndoAction): void {
    if (action === null) {
      return;
    }
    this.undoActions.push(action);
    if (this.undoActions.length > this.Capacity) {
      this.undoActions.shift();
    }
    this.redoActions = [];
  }

  undo(): boolean {
    if (this.undoActions.length === 0) {
      return false;
    }
    const action = this.undoActions.pop();
    if (action === undefined) {
      return false;
    }
    action.Revert();
    this.redoActions.push(action);
    return true;
  }

  redo(): boolean {
    if (this.redoActions.length === 0) {
      return false;
    }
    const action = this.redoActions.pop();
    if (action === undefined) {
      return false;
    }
    action.Apply();
    this.undoActions.push(action);
    return true;
  }

  clear(): void {
    this.undoActions = [];
    this.redoActions = [];
  }
}

class SimulatedAction implements UndoAction {
  Name: string;
  private log: string[];

  constructor(name: string, log: string[]) {
    this.Name = name;
    this.log = log;
  }

  Apply(): void {
    this.log.push('apply:' + this.Name);
  }

  Revert(): void {
    this.log.push('revert:' + this.Name);
  }
}

export interface UndoSimResult {
  canUndo: boolean;
  canRedo: boolean;
  undoCount: number;
  redoCount: number;
  log: string[];
}

export function simulateUndo(ops: string[], capacity: number = 100): UndoSimResult {
  const stack = new UndoStack(capacity);
  const log: string[] = [];
  for (let i = 0; i < ops.length; i++) {
    const op = ops[i];
    if (op.startsWith('push:')) {
      stack.push(new SimulatedAction(op.substring(5), log));
    } else if (op === 'undo') {
      stack.undo();
    } else if (op === 'redo') {
      stack.redo();
    } else if (op === 'clear') {
      stack.clear();
    }
  }
  return {
    canUndo: stack.CanUndo,
    canRedo: stack.CanRedo,
    undoCount: stack.UndoCount,
    redoCount: stack.RedoCount,
    log
  };
}
