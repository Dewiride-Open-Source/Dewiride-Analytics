/**
 * A refusal from the engine, and the one exception every call in this layer throws.
 *
 * Screens never read a status code directly. They ask this object the question they actually care
 * about — was this refused because nobody is signed in, because the details were wrong, because
 * too many attempts have been made — so that the mapping from a number to a meaning is written
 * once here rather than repeated in every component.
 */
export class ApiError extends Error {
  /**
   * The status the engine answered with, or zero when the engine could not be reached at all.
   */
  readonly status: number;

  /** Whatever detail came back with the refusal, when it came back in a shape we understand. */
  readonly problem: ProblemDocument | null;

  constructor(status: number, problem: ProblemDocument | null, message: string, cause?: unknown) {
    super(message, cause === undefined ? undefined : { cause });
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** The engine never answered. Nothing can be said about whether the request took effect. */
  get unreachable(): boolean {
    return this.status === 0;
  }

  /** Nobody is signed in, or the details offered were not accepted. */
  get unauthorised(): boolean {
    return this.status === 401;
  }

  /** The install has already been claimed, so it cannot be claimed again. */
  get alreadyDone(): boolean {
    return this.status === 409;
  }

  /** The caller has used up its allowance and should wait. */
  get throttled(): boolean {
    return this.status === 429;
  }

  /** The individual reasons a submission was refused, when the engine listed them. */
  get reasons(): readonly ProblemReason[] {
    return this.problem?.problems ?? [];
  }
}

/**
 * The refusal document the engine sends, reduced to the parts a screen can use.
 *
 * Anything else it carries — the type, the trace identifier — belongs in a log, not on a screen.
 */
export interface ProblemDocument {
  readonly title?: string;
  readonly detail?: string;
  readonly status?: number;
  readonly problems?: readonly ProblemReason[];
}

/** One named reason a submission was refused. */
export interface ProblemReason {
  readonly code: string;
  readonly description: string;
}

/**
 * Reads a refusal document, accepting that there may not be one.
 *
 * A refusal with an unreadable body is still a refusal, and losing the status because the body
 * was empty would turn a clear answer into an unexplained failure.
 */
export async function readProblem(response: Response): Promise<ProblemDocument | null> {
  try {
    const body: unknown = await response.json();

    return isProblemDocument(body) ? body : null;
  } catch {
    return null;
  }
}

function isProblemDocument(value: unknown): value is ProblemDocument {
  return typeof value === 'object' && value !== null;
}
