import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  useNavigate,
  useParams,
  useSearch,
} from '@tanstack/react-router';

import { AppShell } from '@/components/layout/AppShell';
import { AuthenticationCallbackPage, SignedOutPage, SilentCallbackPage } from '@/pages/AuthenticationCallbackPage';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/Card';
import { DashboardPage } from '@/pages/DashboardPage';
import { FormsPage } from '@/pages/FormsPage';
import { InstanceDetailPage } from '@/pages/InstanceDetailPage';
import { InstancesPage } from '@/pages/InstancesPage';
import { ModelerPage } from '@/pages/ModelerPage';
import { OperationsPage } from '@/pages/OperationsPage';
import { TasksPage } from '@/pages/TasksPage';
import { WorkflowsPage } from '@/pages/WorkflowsPage';

const rootRoute = createRootRoute({
  notFoundComponent: NotFound,
});

/**
 * Die Rueckleitungen des Identity Providers liegen ausserhalb der Anwendungshuelle:
 * Zu diesem Zeitpunkt gibt es noch keine Anmeldung, und die Huelle wuerde sofort
 * wieder zur Anmeldeseite fuehren.
 */
const loginCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/authentication/login-callback',
  component: AuthenticationCallbackPage,
});

const logoutCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/authentication/logout-callback',
  component: SignedOutPage,
});

const silentCallbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/authentication/silent-callback',
  component: SilentCallbackPage,
});

/** Alles Uebrige laeuft in der Anwendungshuelle, die die Anmeldung voraussetzt. */
const shellRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: 'shell',
  component: AppShell,
});

const dashboardRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/',
  component: DashboardPage,
});

const workflowsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/workflows',
  component: Outlet,
});

const workflowsIndexRoute = createRoute({
  getParentRoute: () => workflowsRoute,
  path: '/',
  component: WorkflowsPage,
});

const workflowDetailRoute = createRoute({
  getParentRoute: () => workflowsRoute,
  path: '$definitionId',
  component: WorkflowDetailRoute,
});

function WorkflowDetailRoute() {
  const { definitionId } = useParams({ from: workflowDetailRoute.id });
  return <ModelerPage definitionId={decodeURIComponent(definitionId)} />;
}

const instancesRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/instances',
  component: Outlet,
});

const instancesIndexRoute = createRoute({
  getParentRoute: () => instancesRoute,
  path: '/',
  component: InstancesPage,
});

const instanceDetailRoute = createRoute({
  getParentRoute: () => instancesRoute,
  path: '$instanceId',
  component: InstanceDetailRoute,
});

function InstanceDetailRoute() {
  const { instanceId } = useParams({ from: instanceDetailRoute.id });
  return <InstanceDetailPage instanceId={instanceId} />;
}

const formsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/forms',
  component: FormsPage,
});

const operationsRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/operations',
  component: OperationsPage,
});

interface TasksSearch {
  task?: string;
}

const tasksRoute = createRoute({
  getParentRoute: () => shellRoute,
  path: '/tasks',
  validateSearch: (search: Record<string, unknown>): TasksSearch => ({
    task: typeof search.task === 'string' ? search.task : undefined,
  }),
  component: TasksRoute,
});

function TasksRoute() {
  const { task } = useSearch({ from: tasksRoute.id });
  const navigate = useNavigate();

  return (
    <TasksPage
      selectedTaskId={task}
      onSelectTask={(taskId) =>
        void navigate({ to: '/tasks', search: taskId ? { task: taskId } : {} })
      }
    />
  );
}

function NotFound() {
  const navigate = useNavigate();

  return (
    <div className="grid h-full place-items-center p-10">
      <EmptyState
        icon="explore_off"
        title="Seite nicht gefunden"
        description="Diese Adresse gibt es in der Flowzer Console nicht."
        action={
          <Button variant="primary" icon="home" onClick={() => void navigate({ to: '/' })}>
            Zum Dashboard
          </Button>
        }
      />
    </div>
  );
}

const routeTree = rootRoute.addChildren([
  loginCallbackRoute,
  logoutCallbackRoute,
  silentCallbackRoute,
  shellRoute.addChildren([
    dashboardRoute,
    workflowsRoute.addChildren([workflowsIndexRoute, workflowDetailRoute]),
    instancesRoute.addChildren([instancesIndexRoute, instanceDetailRoute]),
    formsRoute,
    operationsRoute,
    tasksRoute,
  ]),
]);

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  scrollRestoration: true,
});

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
