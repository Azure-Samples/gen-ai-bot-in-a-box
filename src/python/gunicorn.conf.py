import multiprocessing

max_requests = 1000
max_requests_jitter = 50
log_file = "-"
bind = "0.0.0.0:3978"

timeout = 230
# https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/web-apps-performance-faqs#why-does-my-request-time-out-after-230-seconds

num_cpus = multiprocessing.cpu_count()
workers = (num_cpus * 2) + 1
# workers = 1
worker_class = "aiohttp.GunicornWebWorker"
port = 3978